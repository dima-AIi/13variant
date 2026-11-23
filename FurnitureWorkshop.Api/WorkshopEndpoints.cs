using System.Buffers;
using System.Security.Claims;
using System.Text;
using FurnitureWorkshop.Core;
using FurnitureWorkshop.Data;
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

        app.MapPost("/api/customers/import", Import).WithTags("Заказчики")
            .RequireAuthorization("Staff").Accepts<string>("text/csv");
        app.MapGet("/api/orders/export/{format}", Export).WithTags("Заказы").RequireAuthorization("Staff");
    }

    private static async Task<IResult> Login(LoginInput input, IUserRepository users, SessionTokens tokens,
        ILogger<SessionTokens> logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Login) || input.Login.Length > 80 ||
            string.IsNullOrEmpty(input.Password) || input.Password.Length > 128)
            return Results.Unauthorized();

        string login = input.Login.Trim().ToLowerInvariant();
        var user = await users.FindUser(login, ct);
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

    private static async Task<IResult> Import(HttpRequest request, ICustomerRepository store,
        ClaimsPrincipal user, ILogger<ICustomerRepository> logger, CancellationToken ct)
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
                int imported = await store.ImportCustomers(csv, ct);
                logger.LogInformation("Пользователем {UserId} импортировано заказчиков: {ImportedCount}",
                    SessionTokens.User(user).Id, imported);
                return Results.Ok(new ImportResult(imported));
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
        ClaimsPrincipal user, IOrderRepository store, CancellationToken ct)
    {
        format = format.ToLowerInvariant();
        var result = WorkshopFiles.Export(format, await store.FindOrders(SessionTokens.User(user), status, q, ct));
        return Results.File(result.Bytes, result.ContentType, $"orders.{format}");
    }
}
