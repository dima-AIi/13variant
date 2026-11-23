using System.Diagnostics;
using System.Globalization;

namespace FurnitureWorkshop.Core;

public static class OrderRules
{
    public static bool IsFinal(OrderStatus status) =>
        status is OrderStatus.Installed or OrderStatus.Cancelled;

    public static IReadOnlyList<OrderStatus> Next(OrderStatus status) => status switch
    {
        OrderStatus.Measurement => [OrderStatus.Design, OrderStatus.Cancelled],
        OrderStatus.Design => [OrderStatus.Manufacturing, OrderStatus.Cancelled],
        OrderStatus.Manufacturing => [OrderStatus.Delivery, OrderStatus.Cancelled],
        OrderStatus.Delivery => [OrderStatus.Installed, OrderStatus.Cancelled],
        _ => []
    };

    public static void Transition(FurnitureOrder order, OrderStatus target, string? reason, DateTime now)
    {
        if (!Next(order.Status).Contains(target))
            throw new RuleException("Переход из текущего статуса недоступен.");

        if (target == OrderStatus.Manufacturing && order.AssemblerId is null)
            throw new RuleException("Перед изготовлением назначьте сборщика.");

        if (target == OrderStatus.Installed && (order.Amount is not > 0 || order.DeliveryDate is null))
            throw new RuleException("Для установки укажите положительную сумму и дату доставки.");

        if (target == OrderStatus.Cancelled)
            order.CancellationReason = InputRules.Required(reason, "Причина отмены", 500);

        if (target == OrderStatus.Installed)
            order.CompletedAt = now;
        order.Status = target;
        order.Version++;
        Debug.Assert(order.Status != OrderStatus.Installed || order.CompletedAt.HasValue);
        Debug.Assert(order.Status != OrderStatus.Cancelled || !string.IsNullOrWhiteSpace(order.CancellationReason));
    }

    public static WorkshopStatistics Statistics(IReadOnlyList<OrderInfo> rows) =>
        StatisticsFromMetrics(rows.Select(row => new OrderMetrics(row.Status, row.Amount, row.CreatedAt, row.CompletedAt)));

    public static WorkshopStatistics StatisticsFromMetrics(IEnumerable<OrderMetrics> rows)
    {
        var statuses = Enum.GetValues<OrderStatus>();
        var counts = new int[statuses.Length];
        int total = 0, durationCount = 0;
        decimal totalAmount = 0, installedAmount = 0;
        double duration = 0;
        foreach (var row in rows)
        {
            total++;
            if ((uint)row.Status < counts.Length)
                counts[(int)row.Status]++;
            totalAmount += row.Amount ?? 0;
            if (row.Status != OrderStatus.Installed)
                continue;
            installedAmount += row.Amount ?? 0;
            if (row.CompletedAt is { } completed && completed >= row.CreatedAt)
            {
                duration += (completed - row.CreatedAt).TotalDays;
                durationCount++;
            }
        }
        int installed = counts[(int)OrderStatus.Installed], cancelled = counts[(int)OrderStatus.Cancelled];
        return new WorkshopStatistics(total, total - installed - cancelled, installed, cancelled, totalAmount,
            installedAmount, durationCount == 0 ? null : Math.Round(duration / durationCount, 2),
            statuses.Select(status => new StatusCount(status, counts[(int)status])).ToArray());
    }
}

public static class InputRules
{
    public static decimal? ParseAmount(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return null;
        int separator = text.IndexOfAny([',', '.']);
        if ((separator >= 0 && text.Length - separator - 1 is < 1 or > 2) ||
            !decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var amount))
            throw new RuleException("Введите сумму без разделителей тысяч, максимум с двумя знаками после запятой.");
        CheckAmount(amount);
        return amount;
    }

    public static void CheckAmount(decimal? amount)
    {
        if (amount is <= 0 or > 999_999_999 || amount is { } value && value != decimal.Round(value, 2))
            throw new RuleException("Сумма должна быть положительной, до 999 999 999 ₽ и максимум с двумя знаками после запятой.");
    }

    public static DateOnly? ParseDeliveryDate(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
            return null;
        if (DateOnly.TryParseExact(text, ["dd.MM.yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var date))
            return date;
        throw new RuleException("Введите дату доставки в формате ДД.ММ.ГГГГ, например 23.11.2026.");
    }

    public static string Required(string? value, string field, int max)
    {
        var trimmed = Optional(value, field, max);
        if (trimmed.Length == 0)
            throw new RuleException($"Заполните поле «{field}».");
        return trimmed;
    }

    public static string Optional(string? value, string field, int max)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length > max)
            throw new RuleException($"Поле «{field}» допускает до {max} символов.");
        return trimmed;
    }

    public static CustomerInput Customer(CustomerInput input)
    {
        var name = Required(input.Name, "Имя заказчика", 150);
        var phone = Required(input.Phone, "Телефон", 40);
        var email = Optional(input.Email, "Эл. почта", 150);
        if (email.Length != 0 && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            throw new RuleException("Введите корректный адрес электронной почты.");
        return new CustomerInput(name, phone, email);
    }
}
