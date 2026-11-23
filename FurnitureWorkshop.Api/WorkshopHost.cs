using System.Security.Cryptography;
using System.Threading.RateLimiting;
using FurnitureWorkshop.Core;
using FurnitureWorkshop.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace FurnitureWorkshop.Api;

public static class WorkshopHost
{
    public static WebApplication Build(string[] args, bool desktop = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(WorkshopHost).Assembly.GetName().Name
        });

        if (desktop)
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        else if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
            builder.WebHost.UseUrls("http://127.0.0.1:5130");

        var path = DatabasePath(builder.Configuration);
        builder.Services.AddDbContext<WorkshopDb>(options => WorkshopDatabase.Configure(options, path));
        builder.Services.AddScoped<WorkshopStore>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddMemoryCache();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddProblemDetails();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            foreach (var converter in WorkshopFiles.JsonOptions.Converters)
                options.SerializerOptions.Converters.Add(converter);
        });

        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        builder.Services.AddSingleton(new SessionTokens(key));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = SessionTokens.Issuer,
                ValidAudience = SessionTokens.Audience,
                IssuerSigningKey = key,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "login",
                RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var id = context.Principal?.FindFirst("jti")?.Value;
                    var revoked = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                    if (id is null || revoked.TryGetValue(id, out _))
                        context.Fail("Сеанс завершён.");
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorizationBuilder().AddPolicy("Staff", policy =>
            policy.RequireRole(Roles.Admin, Roles.Manager));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("Login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    AutoReplenishment = true
                }));
        });

        builder.Services.AddSerilog((_, logger) => logger.MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(Path.GetDirectoryName(path)!, "logs", "api-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true));

        var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(HandleError));
        app.UseSerilogRequestLogging();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapWorkshopEndpoints();
        return app;
    }

    public static async Task Initialize(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await WorkshopDatabase.Initialize(scope.ServiceProvider.GetRequiredService<WorkshopDb>(),
            DatabasePath(app.Configuration));
    }

    private static string DatabasePath(IConfiguration config) =>
        Path.GetFullPath(config["Workshop:Database"] ?? WorkshopDatabase.DefaultPath);

    private static Task HandleError(HttpContext context)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        return (error switch
        {
            RuleException => Results.Problem(statusCode: 400, title: "Проверьте данные", detail: error.Message),
            BadHttpRequestException bad => Results.Problem(statusCode: bad.StatusCode, title: "Некорректный запрос"),
            DbUpdateConcurrencyException => Results.Problem(statusCode: 409, title: "Заказ изменился",
                detail: "Обновите список, откройте заказ и повторите действие."),
            DbUpdateException => Results.Problem(statusCode: 500, title: "Ошибка сохранения данных"),
            _ => Results.Problem(statusCode: 500, title: "Внутренняя ошибка мастерской")
        }).ExecuteAsync(context);
    }
}
