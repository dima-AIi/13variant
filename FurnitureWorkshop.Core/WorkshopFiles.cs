using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace FurnitureWorkshop.Core;

public static class WorkshopFiles
{
    public const int MaxImportBytes = 1024 * 1024;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static IReadOnlyList<CustomerInput> ReadCustomers(string csv)
    {
        csv = csv.TrimStart('\uFEFF');
        using var parser = new TextFieldParser(new StringReader(csv));
        parser.SetDelimiters(csv.Split('\n', 2)[0].Contains(';') ? ";" : ",");
        try
        {
            var header = parser.ReadFields();
            if (header is null || !header.SequenceEqual(new[] { "Name", "Phone", "Email" }, StringComparer.OrdinalIgnoreCase))
                throw new RuleException("Заголовок CSV должен содержать Name,Phone,Email.");

            var customers = new List<CustomerInput>();
            while (!parser.EndOfData)
            {
                long line = parser.LineNumber;
                var fields = parser.ReadFields()!;
                if (fields.Length != 3)
                    throw new RuleException($"Строка {line}: ожидаются три поля.");
                try
                {
                    customers.Add(InputRules.Customer(new CustomerInput(fields[0], fields[1], fields[2])));
                }
                catch (RuleException error)
                {
                    throw new RuleException($"Строка {line}: {error.Message}");
                }
                if (customers.Count > 5000)
                    throw new RuleException("Один импорт допускает до 5000 заказчиков.");
            }
            if (customers.Count == 0)
                throw new RuleException("В CSV отсутствуют заказчики.");
            return customers;
        }
        catch (MalformedLineException error)
        {
            throw new RuleException($"Некорректный CSV в строке {error.LineNumber}.");
        }
    }

    public static (byte[] Bytes, string ContentType) Export(string format, IReadOnlyList<OrderInfo> orders)
    {
        if (format == "json")
            return (JsonSerializer.SerializeToUtf8Bytes(orders, JsonOptions), "application/json");

        string[] columns = ["Id", "CustomerId", "Customer", "Phone", "Title", "Address", "Notes",
            "AssemblerId", "Assembler", "Status", "Amount", "DeliveryDate", "CancellationReason",
            "CreatedAt", "CompletedAt", "Version"];
        var rows = orders.Select(Row);

        if (format == "xml")
        {
            var root = new XElement("Orders", rows.Select(row =>
                new XElement("Order", columns.Zip(row, (column, value) => new XElement(column, value)))));
            return (Encoding.UTF8.GetBytes(root.ToString()), "application/xml");
        }

        if (format != "csv")
            throw new RuleException("Доступные форматы выгрузки: CSV, JSON и XML.");

        var csv = new StringBuilder("\uFEFF").AppendJoin(';', columns).Append("\r\n");
        foreach (var row in rows)
            csv.AppendJoin(';', row.Select((value, index) => ForCsv(value, index))).Append("\r\n");
        return (Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8");
    }

    private static string?[] Row(OrderInfo order) =>
    [
        Invariant(order.Id), Invariant(order.CustomerId), order.Customer, order.Phone,
        order.Title, order.Address, order.Notes, Invariant(order.AssemblerId), order.Assembler,
        order.Status.ToString(), Invariant(order.Amount), order.DeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        order.CancellationReason, order.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        order.CompletedAt?.ToString("O", CultureInfo.InvariantCulture), Invariant(order.Version)
    ];

    private static string? Invariant(IFormattable? value) => value?.ToString(null, CultureInfo.InvariantCulture);

    private static string ForCsv(string? value, int index)
    {
        value ??= "";
        if (index is 2 or 3 or 4 or 5 or 6 or 8 or 12 &&
            value.TrimStart() is ['=' or '+' or '-' or '@', ..])
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
