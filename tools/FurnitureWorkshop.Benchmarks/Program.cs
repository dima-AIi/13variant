using System.Diagnostics;
using FurnitureWorkshop.Core;
using FurnitureWorkshop.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

string path = Path.Combine(Path.GetTempPath(), $"FurnitureWorkshop.Benchmark.{Guid.NewGuid():N}.db");
var options = new DbContextOptionsBuilder<WorkshopDb>();
WorkshopDatabase.Configure(options, path);
try
{
    await using var db = new WorkshopDb(options.Options);
    await WorkshopDatabase.Initialize(db, path);
    var start = new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    var customer = new Customer { Name = "Заказчик для замеров", Phone = "000" };
    var statuses = Enum.GetValues<OrderStatus>();
    db.AddRange(Enumerable.Range(0, 10_000).Select(i => new FurnitureOrder
    {
        Customer = customer, Title = $"Шкаф {i}", Address = "Учебный адрес", Notes = new string('А', 250),
        AssemblerId = 3, Status = statuses[i % statuses.Length], Amount = 1000m + i / 10m,
        CreatedAt = start.AddDays(-i % 30), CompletedAt = i % statuses.Length == 4 ? start : null, Version = 1
    }));
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    var store = new WorkshopStore(db, TimeProvider.System);
    var manager = new UserInfo(2, "manager", "Менеджер", Roles.Manager);
    var rows = await store.FindOrders(manager, null, null, CancellationToken.None);
    var metrics = await store.FindMetrics(manager, CancellationToken.None);
    var baseline = OldStatistics(rows);
    var revised = OrderRules.StatisticsFromMetrics(metrics);
    if (baseline with { ByStatus = revised.ByStatus } != revised ||
        !baseline.ByStatus.SequenceEqual(revised.ByStatus) || revised.Total != 10_000)
        throw new InvalidOperationException("Итоги двух реализаций статистики отличаются.");

    Measure("Core: несколько проходов", () => OldStatistics(rows));
    Measure("Core: один проход", () => OrderRules.StatisticsFromMetrics(metrics));
    Measure("SQLite: полная карточка с JOIN", () =>
        store.FindOrders(manager, null, null, CancellationToken.None).GetAwaiter().GetResult());
    Measure("SQLite: четыре поля статистики", () =>
        store.FindMetrics(manager, CancellationToken.None).GetAwaiter().GetResult());
}
finally
{
    SqliteConnection.ClearAllPools();
    File.Delete(path);
}

static void Measure<T>(string title, Func<T> work)
{
    for (int i = 0; i < 8; i++)
        GC.KeepAlive(work());
    var elapsed = new double[31];
    var allocated = new long[31];
    for (int i = 0; i < elapsed.Length; i++)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        GC.KeepAlive(work());
        elapsed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
    }
    Array.Sort(elapsed);
    Array.Sort(allocated);
    Console.WriteLine(FormattableString.Invariant($"{title}: {elapsed[15]:F3} ms, {allocated[15]} B"));
}

static WorkshopStatistics OldStatistics(IReadOnlyList<OrderInfo> rows)
{
    var installed = rows.Where(row => row.Status == OrderStatus.Installed).ToArray();
    var durations = installed.Where(row => row.CompletedAt.HasValue)
        .Select(row => (row.CompletedAt!.Value - row.CreatedAt).TotalDays).ToArray();
    return new WorkshopStatistics(rows.Count, rows.Count(row => !OrderRules.IsFinal(row.Status)),
        installed.Length, rows.Count(row => row.Status == OrderStatus.Cancelled),
        rows.Sum(row => row.Amount ?? 0), installed.Sum(row => row.Amount ?? 0),
        durations.Length == 0 ? null : Math.Round(durations.Average(), 2),
        Enum.GetValues<OrderStatus>().Select(status =>
            new StatusCount(status, rows.Count(row => row.Status == status))).ToArray());
}
