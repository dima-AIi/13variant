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
    }

    public static WorkshopStatistics Statistics(IReadOnlyList<OrderInfo> rows)
    {
        var installed = rows.Where(row => row.Status == OrderStatus.Installed).ToArray();
        var durations = installed.Where(row => row.CompletedAt.HasValue)
            .Select(row => (row.CompletedAt!.Value - row.CreatedAt).TotalDays).ToArray();
        return new WorkshopStatistics(rows.Count, rows.Count(row => !IsFinal(row.Status)),
            installed.Length, rows.Count(row => row.Status == OrderStatus.Cancelled),
            rows.Sum(row => row.Amount ?? 0), installed.Sum(row => row.Amount ?? 0),
            durations.Length == 0 ? null : Math.Round(durations.Average(), 2),
            Enum.GetValues<OrderStatus>().Select(status =>
                new StatusCount(status, rows.Count(row => row.Status == status))).ToArray());
    }
}

public static class InputRules
{
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
