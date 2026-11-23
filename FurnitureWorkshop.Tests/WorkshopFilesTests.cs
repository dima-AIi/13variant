using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FurnitureWorkshop.Core;
using Xunit;

namespace FurnitureWorkshop.Tests;

public sealed class WorkshopFilesTests
{
    [Theory]
    [InlineData(',')]
    [InlineData(';')]
    public void ReadsQuotedCsvAndUtf8Bom(char separator)
    {
        string input = $"\uFEFFName{separator}Phone{separator}Email\r\n\"Дуб, клён; мастерская\"{separator}+79990001234{separator}test@example.com";
        var customer = Assert.Single(WorkshopFiles.ReadCustomers(input));
        Assert.Equal("Дуб, клён; мастерская", customer.Name);
    }

    [Theory]
    [InlineData("Wrong,Phone,Email\nМебель,123,")]
    [InlineData("Name,Phone,Email\nМебель,,")]
    [InlineData("Name,Phone,Email\nМебель,123,bad-email")]
    [InlineData("Name,Phone,Email\n\"незакрытая кавычка,123,")]
    [InlineData("Name,Phone,Email")]
    public void RejectsInvalidCsv(string input) =>
        Assert.ThrowsAny<RuleException>(() => WorkshopFiles.ReadCustomers(input));

    [Fact]
    public void ExportsThreeReadableFormats()
    {
        var row = OrderRulesTests.Row(OrderStatus.Design, 4200.5m, DateTime.UnixEpoch, null);
        Assert.Equal(row, Assert.Single(JsonSerializer.Deserialize<OrderInfo[]>(
            WorkshopFiles.Export("json", [row]).Bytes, WorkshopFiles.JsonOptions)!));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(WorkshopFiles.Export("xml", [row]).Bytes));
        Assert.Equal(row.Title, xml.Root!.Element("Order")!.Element("Title")!.Value);
        var csv = Encoding.UTF8.GetString(WorkshopFiles.Export("csv", [row]).Bytes);
        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("\"Шкаф \"\"Лофт\"\" <дуб>\"", csv);
        Assert.Contains("\"'+79990001234\"", csv);
        Assert.Contains("\"4200.5\"", csv);
    }
}
