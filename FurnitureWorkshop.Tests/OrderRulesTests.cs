using FurnitureWorkshop.Core;
using Xunit;

namespace FurnitureWorkshop.Tests;

public sealed class OrderRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(OrderStatus.Measurement, OrderStatus.Design)]
    [InlineData(OrderStatus.Design, OrderStatus.Manufacturing)]
    [InlineData(OrderStatus.Manufacturing, OrderStatus.Delivery)]
    [InlineData(OrderStatus.Delivery, OrderStatus.Installed)]
    public void AdvancesThroughEachStage(OrderStatus from, OrderStatus to)
    {
        var order = Ready(from);
        OrderRules.Transition(order, to, null, Now);
        Assert.Equal(to, order.Status);
        Assert.Equal(2, order.Version);
        Assert.Equal(to == OrderStatus.Installed ? Now : (DateTime?)null, order.CompletedAt);
    }

    [Theory]
    [InlineData(OrderStatus.Measurement)]
    [InlineData(OrderStatus.Design)]
    [InlineData(OrderStatus.Manufacturing)]
    [InlineData(OrderStatus.Delivery)]
    public void CancelsOpenOrdersWithReason(OrderStatus from)
    {
        var order = Ready(from);
        Assert.Throws<RuleException>(() => OrderRules.Transition(order, OrderStatus.Cancelled, "  ", Now));
        OrderRules.Transition(order, OrderStatus.Cancelled, "  Изменились размеры  ", Now);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Изменились размеры", order.CancellationReason);
    }

    [Theory]
    [InlineData(OrderStatus.Measurement, OrderStatus.Delivery)]
    [InlineData(OrderStatus.Manufacturing, OrderStatus.Design)]
    [InlineData(OrderStatus.Installed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Measurement)]
    public void RejectsInvalidTransitions(OrderStatus from, OrderStatus to)
    {
        var order = Ready(from);
        Assert.Throws<RuleException>(() => OrderRules.Transition(order, to, "Причина", Now));
        Assert.Equal(from, order.Status);
        Assert.Equal(1, order.Version);
    }

    [Fact]
    public void ManufacturingRequiresAssembler()
    {
        var order = Ready(OrderStatus.Design);
        order.AssemblerId = null;
        Assert.Throws<RuleException>(() => OrderRules.Transition(order, OrderStatus.Manufacturing, null, Now));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InstallationRequiresAmountAndDate(bool positiveAmount, bool hasDate)
    {
        var order = Ready(OrderStatus.Delivery);
        order.Amount = positiveAmount ? 5000 : 0;
        order.DeliveryDate = hasDate ? DateOnly.FromDateTime(Now) : null;
        Assert.Throws<RuleException>(() => OrderRules.Transition(order, OrderStatus.Installed, null, Now));
    }

    [Fact]
    public void StatisticsUsesInstalledOrdersForAverage()
    {
        var rows = new[] { Row(OrderStatus.Installed, 6000, Now.AddDays(-1.5), Now),
            Row(OrderStatus.Installed, 4000, Now.AddDays(-2.5), Now),
            Row(OrderStatus.Cancelled, 1000, Now.AddDays(-5), null),
            Row(OrderStatus.Design, 2000, Now.AddDays(-10), null) };
        var statistics = OrderRules.Statistics(rows);
        Assert.Equal(4, statistics.Total);
        Assert.Equal(1, statistics.Open);
        Assert.Equal(2, statistics.Installed);
        Assert.Equal(1, statistics.Cancelled);
        Assert.Equal(13000, statistics.TotalAmount);
        Assert.Equal(10000, statistics.InstalledAmount);
        Assert.Equal(2, statistics.AverageDays);
        Assert.Equal(6, statistics.ByStatus.Length);
        Assert.Null(OrderRules.Statistics([]).AverageDays);
    }

    private static FurnitureOrder Ready(OrderStatus status) => new()
    {
        Status = status, AssemblerId = 3, Amount = 5000, DeliveryDate = DateOnly.FromDateTime(Now), Version = 1
    };

    internal static OrderInfo Row(OrderStatus status, decimal amount, DateTime created, DateTime? completed) =>
        new(1, 1, "ООО «Мебель; дом»", "+79990001234", "Шкаф \"Лофт\" <дуб>", "Ул. Парковая, 10", "",
            3, "Сборщик", status, amount, null, null, created, completed, 1);
}
