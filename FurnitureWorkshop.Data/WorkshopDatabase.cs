using FurnitureWorkshop.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Security.Cryptography;

namespace FurnitureWorkshop.Data;

public static class WorkshopDatabase
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FurnitureWorkshop", "workshop.db");

    public static void Configure(DbContextOptionsBuilder options, string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            DefaultTimeout = 10
        }.ToString();
        options.UseSqlite(connectionString).AddInterceptors(new SearchFunctions());
    }

    public static async Task Initialize(WorkshopDb db, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await db.Database.MigrateAsync(ct);
    }

    public static string Hash(string password, string salt) => Convert.ToHexString(
        Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(salt), 210_000, HashAlgorithmName.SHA256, 32));

    public static bool Verify(string password, string passwordHash)
    {
        var parts = passwordHash.Split(':', 2);
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(password, parts[0])),
            Convert.FromHexString(parts[1]));
    }

    private sealed class SearchFunctions : Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor
    {
        public override void ConnectionOpened(System.Data.Common.DbConnection connection,
            Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData) => Register(connection);

        public override Task ConnectionOpenedAsync(System.Data.Common.DbConnection connection,
            Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Register(connection);
            return Task.CompletedTask;
        }

        private static void Register(System.Data.Common.DbConnection connection) =>
            ((SqliteConnection)connection).CreateFunction<string, string, bool>("workshop_contains",
                (value, term) => value.Contains(term, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
    }
}

public sealed class WorkshopDbFactory : IDesignTimeDbContextFactory<WorkshopDb>
{
    public WorkshopDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorkshopDb>();
        WorkshopDatabase.Configure(options, WorkshopDatabase.DefaultPath);
        return new WorkshopDb(options.Options);
    }
}
