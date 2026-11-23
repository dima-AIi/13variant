using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FurnitureWorkshop.Core;
using FurnitureWorkshop.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FurnitureWorkshop.Tests;

public sealed class ApiTests
{
    private static string DemoPassword(string account) =>
        Environment.GetEnvironmentVariable($"WORKSHOP_DEMO_{account}_PASSWORD")
        ?? throw new InvalidOperationException($"Set WORKSHOP_DEMO_{account}_PASSWORD before running integration tests.");

    [Fact]
    public async Task UnreadableStoredHashRejectsLoginWithoutServerError()
    {
        using var server = new DatabaseFactory();
        using var client = server.CreateClient();
        await using var scope = server.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkshopDb>();
        var user = await db.Users.FindAsync(2);
        user!.PasswordHash = "invalid-hash";
        await db.SaveChangesAsync();

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginInput("manager", DemoPassword("MANAGER")));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("invalid-hash", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AccessIsEnforcedForAllRolesAndLogoutRevokesToken()
    {
        using var server = new DatabaseFactory();
        using var anonymous = server.CreateClient();
        using var admin = await SignIn(server, "admin", DemoPassword("ADMIN"), Roles.Admin);
        using var manager = await SignIn(server, "manager", DemoPassword("MANAGER"), Roles.Manager);
        using var first = await SignIn(server, "ivan", DemoPassword("IVAN"), Roles.Assembler);
        using var second = await SignIn(server, "pavel", DemoPassword("PAVEL"), Roles.Assembler);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(4, (await Get<UserInfo[]>(admin, "/api/users")).Length);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(2, (await Get<UserInfo[]>(manager, "/api/users/assemblers")).Length);

        int customer = await CreateCustomer(manager);
        int order1 = await CreateOrder(manager, customer, 3, "Шкаф");
        int order2 = await CreateOrder(admin, customer, 4, "Гардероб");

        Assert.Equal(2, (await Get<OrderInfo[]>(admin, "/api/orders")).Length);
        Assert.Equal(order1, Assert.Single(await Get<OrderInfo[]>(first, "/api/orders")).Id);
        Assert.Equal(order2, Assert.Single(await Get<OrderInfo[]>(second, "/api/orders")).Id);
        Assert.Equal(1, (await Get<WorkshopStatistics>(first, "/api/orders/statistics")).Total);
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync($"/api/orders/{order2}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await Post(first, $"/api/orders/{order2}/transition",
            new TransitionInput(OrderStatus.Design, null, 1)));
        Assert.Equal(HttpStatusCode.Forbidden, await Post(first, "/api/orders", new { }));
        Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync("/api/orders/export/json")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await Post(first, $"/api/orders/{order1}/transition",
            new TransitionInput(OrderStatus.Design, null, 1)));

        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task OrderAdvancesWithValidationAndRejectsStaleEdits()
    {
        using var server = new DatabaseFactory();
        using var manager = await SignIn(server, "manager", DemoPassword("MANAGER"), Roles.Manager);
        int customer = await CreateCustomer(manager);
        int order = await CreateOrder(manager, customer, null, "Гардероб");
        var path = $"/api/orders/{order}";
        var state = await Get<OrderInfo>(manager, path);
        Assert.Equal(OrderStatus.Measurement, state.Status);
        Assert.Equal(HttpStatusCode.BadRequest, await Step(OrderStatus.Delivery, 1));
        Assert.Equal(HttpStatusCode.NoContent, await Step(OrderStatus.Design, 1));
        Assert.Equal(HttpStatusCode.Conflict, await Step(OrderStatus.Manufacturing, 1));
        Assert.Equal(HttpStatusCode.BadRequest, await Step(OrderStatus.Manufacturing, 2));
        Assert.Equal(HttpStatusCode.NoContent, await Edit(2, 3, null, null));
        Assert.Equal(HttpStatusCode.NoContent, await Step(OrderStatus.Manufacturing, 3));
        Assert.Equal(HttpStatusCode.BadRequest, await Edit(4, null, null, null));
        Assert.Equal(HttpStatusCode.NoContent, await Step(OrderStatus.Delivery, 4));
        Assert.Equal(HttpStatusCode.BadRequest, await Step(OrderStatus.Installed, 5));
        Assert.Equal(HttpStatusCode.NoContent, await Edit(5, 3, 36000, new DateOnly(2026, 11, 21)));
        Assert.Equal(HttpStatusCode.NoContent, await Step(OrderStatus.Installed, 6));
        Assert.Equal(HttpStatusCode.BadRequest, await Step(OrderStatus.Cancelled, 7, "Передумал"));
        var statistics = await Get<WorkshopStatistics>(manager, "/api/orders/statistics");
        Assert.Equal(36000, statistics.InstalledAmount);
        Assert.NotNull(statistics.AverageDays);

        int cancelled = await CreateOrder(manager, customer, 4, "Комод");
        Assert.Equal(HttpStatusCode.BadRequest, await Post(manager, $"/api/orders/{cancelled}/transition",
            new TransitionInput(OrderStatus.Cancelled, " ", 1)));
        Assert.Equal(HttpStatusCode.NoContent, await Post(manager, $"/api/orders/{cancelled}/transition",
            new TransitionInput(OrderStatus.Cancelled, "Отказ", 1)));
        Assert.Equal("Отказ", (await Get<OrderInfo>(manager, $"/api/orders/{cancelled}")).CancellationReason);

        Task<HttpStatusCode> Step(OrderStatus target, int version, string? reason = null) =>
            Post(manager, path + "/transition", new TransitionInput(target, reason, version));

        async Task<HttpStatusCode> Edit(int version, int? assembler, decimal? amount, DateOnly? date)
        {
            using var response = await manager.PutAsJsonAsync(path,
                new OrderInput(customer, "Гардероб", "Ул. Парковая", null, assembler, amount, date, version),
                WorkshopFiles.JsonOptions);
            return response.StatusCode;
        }
    }

    [Fact]
    public async Task ImportsAtomicallyAndExportsFilteredOrders()
    {
        using var server = new DatabaseFactory();
        using var manager = await SignIn(server, "manager", DemoPassword("MANAGER"), Roles.Manager);
        using var wrong = new StringContent("Name;Phone;Email\nЗаказчик;123;\nОшибка;;", Encoding.UTF8, "text/csv");
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync("/api/customers/import", wrong)).StatusCode);
        Assert.Empty(await Get<CustomerInfo[]>(manager, "/api/customers"));

        using var csv = new StringContent("Name;Phone;Email\n\"Орлова, Анна\";123;anna@example.com\nПетров;456;",
            Encoding.UTF8, "text/csv");
        using var imported = await manager.PostAsync("/api/customers/import", csv);
        Assert.Equal(2, (await imported.Content.ReadFromJsonAsync<ImportResult>())!.Imported);

        var customers = await Get<CustomerInfo[]>(manager, "/api/customers");
        int exportId = await CreateOrder(manager, customers[0].Id, 3, "Кухня");
        await CreateOrder(manager, customers[1].Id, 4, "Шкаф");
        var query = "?status=Measurement&q=" + Uri.EscapeDataString("кух");
        Assert.Equal(exportId, Assert.Single(await Get<OrderInfo[]>(manager, "/api/orders" + query)).Id);

        foreach (string format in new[] { "csv", "json", "xml" })
        {
            using var response = await manager.GetAsync($"/api/orders/export/{format}{query}");
            response.EnsureSuccessStatusCode();
            Assert.Equal($"orders.{format}", response.Content.Headers.ContentDisposition!.FileName);
            var document = await response.Content.ReadAsStringAsync();
            Assert.Contains("Кухня", format == "json"
                ? Assert.Single(System.Text.Json.JsonSerializer.Deserialize<OrderInfo[]>(document, WorkshopFiles.JsonOptions)!).Title
                : document);
            Assert.DoesNotContain("Шкаф", document);
        }
    }

    private static async Task<HttpClient> SignIn(DatabaseFactory server, string login, string password, string role)
    {
        var http = server.CreateClient();
        using var response = await http.PostAsJsonAsync("/api/auth/login", new LoginInput(login, password));
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<LoginResult>())!;
        Assert.Equal(role, session.User.Role);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        return http;
    }

    private static async Task<int> CreateCustomer(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/customers", new CustomerInput("Заказчик", "123", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<int> CreateOrder(HttpClient client, int customer, int? assembler, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/orders",
            new OrderInput(customer, title, "Ул. Парковая", null, assembler, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<T> Get<T>(HttpClient http, string url) =>
        (await http.GetFromJsonAsync<T>(url, WorkshopFiles.JsonOptions))!;

    private static async Task<HttpStatusCode> Post(HttpClient http, string url, object value)
    {
        using var result = await http.PostAsJsonAsync(url, value, WorkshopFiles.JsonOptions);
        return result.StatusCode;
    }

    private sealed record Created(int Id);

    private sealed class DatabaseFactory : WebApplicationFactory<Program>
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "FurnitureWorkshop.Tests", Guid.NewGuid().ToString("N"));

        protected override IHost CreateHost(IHostBuilder builder)
        {
            const string setting = "Workshop__Database";
            string? previous = Environment.GetEnvironmentVariable(setting);
            try
            {
                Environment.SetEnvironmentVariable(setting, Path.Combine(directory, "integration.db"));
                return base.CreateHost(builder);
            }
            finally
            {
                Environment.SetEnvironmentVariable(setting, previous);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(directory))
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
