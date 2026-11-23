using FurnitureWorkshop.Core;
using Microsoft.EntityFrameworkCore;

namespace FurnitureWorkshop.Data;

internal static class WorkshopDemo
{
    public static async Task Seed(DbContext db, CancellationToken ct)
    {
        if (await db.Set<Customer>().AnyAsync(ct) || await db.Set<FurnitureOrder>().AnyAsync(ct))
            return;

        var start = new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
        Customer[] customers =
        [
            new() { Name = "Анна Белова", Phone = "+7 000 000-01-01", RegisteredAt = start },
            new() { Name = "Роман Орлов", Phone = "+7 000 000-01-02", RegisteredAt = start },
            new() { Name = "Мария Соколова", Phone = "+7 000 000-01-03", RegisteredAt = start }
        ];

        db.AddRange(
            Order(0, "Стеллаж в гостиную", OrderStatus.Measurement),
            Order(1, "Шкаф в прихожую", OrderStatus.Design, 3, 78_000),
            Order(2, "Кухонный гарнитур", OrderStatus.Manufacturing, 4, 192_000),
            Order(3, "Обеденный стол", OrderStatus.Delivery, 4, 42_900),
            Order(4, "Угловой шкаф", OrderStatus.Installed, 3, 124_200, 16),
            Order(5, "Кухня с островом", OrderStatus.Installed, 4, 156_000, 19),
            Order(6, "Полка под книги", OrderStatus.Cancelled, null, null));
        await db.SaveChangesAsync(ct);

        FurnitureOrder Order(int index, string title, OrderStatus status, int? assembler = null,
            decimal? amount = null, int? days = null) => new()
        {
            Customer = customers[index % customers.Length],
            Title = title,
            Address = $"Учебный адрес, дом {index + 1}",
            Status = status,
            AssemblerId = assembler,
            Amount = amount,
            DeliveryDate = days is { } delay ? DateOnly.FromDateTime(start.AddDays(index + delay)) :
                status == OrderStatus.Delivery ? DateOnly.FromDateTime(start.AddDays(14)) : null,
            CreatedAt = start.AddDays(index),
            CompletedAt = days is { } duration ? start.AddDays(index + duration) : null,
            CancellationReason = status == OrderStatus.Cancelled ? "Заказчик изменил планы" : null,
            Version = 1
        };
    }
}
