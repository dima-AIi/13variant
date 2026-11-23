using System.Buffers;
using System.Security.Claims;
using System.Text;
using FurnitureWorkshop.Core;
using FurnitureWorkshop.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FurnitureWorkshop.Api;

internal static class WorkshopEndpoints
{
    public static void MapWorkshopEndpoints(this WebApplication app)
    {
        app.MapGet("/health", async (WorkshopDb db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok(new HealthInfo("ok")) :
                Results.Problem(statusCode: 503, title: "База данных недоступна"));

        var auth = app.MapGroup("/api/auth").WithTags("Аутентификация");
        auth.MapPost("/login", Login).RequireRateLimiting("Login");
        auth.MapGet("/me", (ClaimsPrincipal user) => SessionTokens.User(user)).RequireAuthorization();
        auth.MapPost("/logout", (ClaimsPrincipal user, IMemoryCache cache) =>
        {
            SessionTokens.Revoke(user, cache);
            return Results.NoContent();
        }).RequireAuthorization();

        var customers = app.MapGroup("/api/customers").WithTags("Заказчики").RequireAuthorization("Staff");
        customers.MapGet("", (WorkshopStore store, CancellationToken ct) => store.GetCustomers(ct));
        customers.MapPost("", async (CustomerInput input, WorkshopStore store, CancellationToken ct) =>
        {
            int id = await store.CreateCustomer(input, ct);
            return Results.Created("/api/customers", new { id });
        });
        customers.MapPut("/{id:int}", async (int id, CustomerInput input, WorkshopStore store, CancellationToken ct) =>
            await store.UpdateCustomer(id, input, ct) ? Results.NoContent() : Results.NotFound());
        customers.MapPost("/import", Import).Accepts<string>("text/csv");

        app.MapGet("/api/users/assemblers", (WorkshopStore store, CancellationToken ct) => store.GetUsers(true, ct))
            .WithTags("Пользователи").RequireAuthorization("Staff");
        app.MapGet("/api/users", (WorkshopStore store, CancellationToken ct) => store.GetUsers(false, ct))
            .WithTags("Пользователи").RequireAuthorization(new AuthorizeAttribute { Roles = Roles.Admin });

        var orders = app.MapGroup("/api/orders").WithTags("Заказы").RequireAuthorization();
        orders.MapGet("", (OrderStatus? status, string? q, ClaimsPrincipal user, WorkshopStore store, CancellationToken ct) =>
            store.FindOrders(SessionTokens.User(user), status, q, ct));
        orders.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, WorkshopStore store, CancellationToken ct) =>
            await store.FindOrder(id, SessionTokens.User(user), ct) is { } row ? Results.Ok(row) : Results.NotFound());
        orders.MapPost("", async (OrderInput input, WorkshopStore store, CancellationToken ct) =>
        {
            int id = await store.CreateOrder(input, ct);
            return Results.Created($"/api/orders/{id}", new { id });
        }).RequireAuthorization("Staff");
        orders.MapPut("/{id:int}", async (int id, OrderInput input, WorkshopStore store, CancellationToken ct) =>
            await store.UpdateOrder(id, input, ct) ? Results.NoContent() : Results.NotFound()).RequireAuthorization("Staff");
        orders.MapPost("/{id:int}/transition", async (int id, TransitionInput input, ClaimsPrincipal user,
            WorkshopStore store, CancellationToken ct) =>
            await store.Transition(id, SessionTokens.User(user), input, ct) ? Results.NoContent() : Results.NotFound());
        orders.MapGet("/statistics", async (ClaimsPrincipal user, WorkshopStore store, CancellationToken ct) =>
            OrderRules.Statistics(await store.FindOrders(SessionTokens.User(user), null, null, ct)));
        orders.MapGet("/export/{format}", Export).RequireAuthorization("Staff");
    }

    private static async Task<IResult> Login(LoginInput input, WorkshopDb db, SessionTokens tokens,
        ILogger<SessionTokens> logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Login) || input.Login.Length > 80 ||
            string.IsNullOrEmpty(input.Password) || input.Password.Length > 128)
            return Results.Unauthorized();

        string login = input.Login.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Login == login, ct);
        const string dummyHash = "00000000000000000000000000000000:0000000000000000000000000000000000000000000000000000000000000000";
        if (!WorkshopDatabase.Verify(input.Password, user?.PasswordHash ?? dummyHash) || user is null)
        {
            logger.LogWarning("Неудачная попытка авторизации");
            return Results.Unauthorized();
        }

        var result = new UserInfo(user.Id, user.Login, user.DisplayName, user.Role);
        logger.LogInformation("Авторизация пользователя {UserId} с ролью {Role}", user.Id, user.Role);
        return Results.Ok(new LoginResult(tokens.Issue(result), result));
    }

    private static async Task<IResult> Import(HttpRequest request, WorkshopStore store, CancellationToken ct)
    {
        if (request.ContentLength > WorkshopFiles.MaxImportBytes)
            return Results.Problem(statusCode: 413, title: "Размер CSV превышает 1 МБ.");

        using var buffer = new MemoryStream();
        byte[] chunk = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int count;
            while ((count = await request.Body.ReadAsync(chunk.AsMemory(), ct)) > 0)
            {
                if (buffer.Length + count > WorkshopFiles.MaxImportBytes)
                    return Results.Problem(statusCode: 413, title: "Размер CSV превышает 1 МБ.");
                buffer.Write(chunk, 0, count);
            }
            try
            {
                var csv = new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                return Results.Ok(new ImportResult(await store.ImportCustomers(csv, ct)));
            }
            catch (DecoderFallbackException)
            {
                throw new RuleException("Сохраните CSV в кодировке UTF-8.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk, clearArray: true);
        }
    }

    private static async Task<IResult> Export(string format, OrderStatus? status, string? q,
        ClaimsPrincipal user, WorkshopStore store, CancellationToken ct)
    {
        format = format.ToLowerInvariant();
        var result = WorkshopFiles.Export(format, await store.FindOrders(SessionTokens.User(user), status, q, ct));
        return Results.File(result.Bytes, result.ContentType, $"orders.{format}");
    }
}
