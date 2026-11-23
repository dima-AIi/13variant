using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using FurnitureWorkshop.Core;
using Xunit;

namespace FurnitureWorkshop.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("en-US", "1500.50")]
    [InlineData("en-US", "1500,50")]
    [InlineData("ru-RU", "1500.50")]
    [InlineData("ru-RU", "1500,50")]
    [InlineData("de-DE", "1500,50")]
    public void AmountIsIndependentOfRegion(string region, string text)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(region);
            Assert.Equal(1500.50m, InputRules.ParseAmount(text));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("1,500")]
    [InlineData("1 500")]
    [InlineData("1,500.50")]
    [InlineData("4,")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000000000")]
    public void UnclearOrOutOfRangeAmountIsRejected(string text) =>
        Assert.Throws<RuleException>(() => InputRules.ParseAmount(text));

    [Theory]
    [InlineData("29.02.2024")]
    [InlineData("2024-02-29")]
    public void DeliveryAcceptsBothDocumentedFormats(string text) =>
        Assert.Equal(new DateOnly(2024, 2, 29), InputRules.ParseDeliveryDate(text));

    [Theory]
    [InlineData("29.02.2026")]
    [InlineData("02/03/2026")]
    [InlineData("2026-11-31")]
    public void InvalidOrAmbiguousDeliveryIsRejected(string text) =>
        Assert.Throws<RuleException>(() => InputRules.ParseDeliveryDate(text));

    [Theory]
    [InlineData(149, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    [InlineData(0, false)]
    public void CustomerNameBoundary(int length, bool valid)
    {
        var input = new CustomerInput(new string('А', length), "123", null);
        if (valid) Assert.Equal(input.Name, InputRules.Customer(input).Name);
        else Assert.Throws<RuleException>(() => InputRules.Customer(input));
    }

    [Theory]
    [InlineData(39, true)]
    [InlineData(40, true)]
    [InlineData(41, false)]
    [InlineData(0, false)]
    public void CustomerPhoneBoundary(int length, bool valid)
    {
        var input = new CustomerInput("Заказчик", new string('1', length), null);
        if (valid) Assert.Equal(input.Phone, InputRules.Customer(input).Phone);
        else Assert.Throws<RuleException>(() => InputRules.Customer(input));
    }

    [Fact]
    public void StatisticsIgnoresInvalidDurationButKeepsAmountAndCount()
    {
        var start = new DateTime(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc);
        OrderMetrics[] orders =
        [
            new(OrderStatus.Installed, 6.24m, start, start.AddDays(1.5)),
            new(OrderStatus.Installed, 3.01m, start, start.AddDays(-1)),
            new(OrderStatus.Installed, null, start, null),
            new(OrderStatus.Cancelled, 2.75m, start, start.AddDays(15))
        ];
        var result = OrderRules.StatisticsFromMetrics(orders);
        Assert.Equal(1.5, result.AverageDays);
        Assert.Equal(4, result.Total);
        Assert.Equal(3, result.Installed);
        Assert.Equal(9.25m, result.InstalledAmount);
        Assert.Equal(12.00m, result.TotalAmount);
        Assert.Equal(4, result.ByStatus.Sum(row => row.Count));
    }

    [Fact]
    public void CsvReportsPhysicalLineNumbersAfterBlankLines()
    {
        const string csv = "\nName;Phone;Email\n\nЗапись;100;\n\nОшибочная;;невозможно";
        var error = Assert.Throws<CsvImportException>(() => WorkshopFiles.ReadCustomers(csv));
        Assert.Equal(6, Assert.Single(error.Errors).Line);
    }

    [Fact]
    public void ExportTimestampsHaveUtcZone()
    {
        var start = new DateTime(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc);
        var row = OrderRulesTests.Row(OrderStatus.Installed, 10m, start, start.AddHours(6));
        using var json = JsonDocument.Parse(WorkshopFiles.Export("json", [row]).Bytes);
        Assert.Equal("2026-11-08T03:00:00Z", json.RootElement[0].GetProperty("createdAt").GetString());
        var xml = XElement.Load(new MemoryStream(WorkshopFiles.Export("xml", [row]).Bytes));
        Assert.EndsWith("Z", xml.Element("Order")!.Element("CompletedAt")!.Value, StringComparison.Ordinal);
    }
}
