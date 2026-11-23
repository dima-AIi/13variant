namespace FurnitureWorkshop.Core;

public static class WorkshopText
{
    public static string Status(OrderStatus value) => value switch
    {
        OrderStatus.Measurement => "Замер",
        OrderStatus.Design => "Проект",
        OrderStatus.Manufacturing => "Изготовление",
        OrderStatus.Delivery => "Доставка",
        OrderStatus.Installed => "Установлен",
        OrderStatus.Cancelled => "Отменён",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string Role(string role) => role switch
    {
        Roles.Admin => "Администратор",
        Roles.Manager => "Менеджер",
        Roles.Assembler => "Сборщик",
        _ => role
    };
}
