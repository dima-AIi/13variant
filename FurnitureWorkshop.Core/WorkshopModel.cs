namespace FurnitureWorkshop.Core;

public enum OrderStatus { Measurement, Design, Manufacturing, Delivery, Installed, Cancelled }
public enum UserRole { Admin, Manager, Assembler }

public static class Roles
{
    public const string Admin = nameof(UserRole.Admin);
    public const string Manager = nameof(UserRole.Manager);
    public const string Assembler = nameof(UserRole.Assembler);
    public const string Staff = Admin + "," + Manager;
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime? RegisteredAt { get; set; }
}

public sealed class WorkshopUser
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public sealed class FurnitureOrder
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public int? AssemblerId { get; set; }
    public WorkshopUser? Assembler { get; set; }
    public OrderStatus Status { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Version { get; set; }
}

public sealed record CustomerInput(string Name, string Phone, string? Email);
public sealed record CustomerInfo(int Id, string Name, string Phone, string Email);
public sealed record UserInfo(int Id, string Login, string DisplayName, string Role);
public sealed record LoginInput(string Login, string Password);
public sealed record LoginResult(string Token, UserInfo User);
public sealed record OrderInput(int CustomerId, string Title, string Address, string? Notes,
    int? AssemblerId, decimal? Amount, DateOnly? DeliveryDate, int? Version = null);
public sealed record TransitionInput(OrderStatus Status, string? Reason, int Version);
public sealed record OrderInfo(int Id, int CustomerId, string Customer, string Phone, string Title,
    string Address, string Notes, int? AssemblerId, string? Assembler, OrderStatus Status,
    decimal? Amount, DateOnly? DeliveryDate, string? CancellationReason, DateTime CreatedAt,
    DateTime? CompletedAt, int Version);
public sealed record StatusCount(OrderStatus Status, int Count);
public sealed record WorkshopStatistics(int Total, int Open, int Installed, int Cancelled,
    decimal TotalAmount, decimal InstalledAmount, double? AverageDays, StatusCount[] ByStatus);
public sealed record ImportResult(int Imported);
public sealed record HealthInfo(string Status);

public class RuleException(string message) : Exception(message);
public sealed class OrderConflictException() : Exception("Обновите список, откройте заказ и повторите действие.");
public sealed record CsvRowError(long Line, string Message);
public sealed class CsvImportException(IReadOnlyList<CsvRowError> errors) :
    RuleException(string.Join(Environment.NewLine, errors.Take(10).Select(row => $"Строка {row.Line}: {row.Message}"))
        + (errors.Count > 10 ? $"\nВсего ошибок: {errors.Count}." : ""))
{
    public IReadOnlyList<CsvRowError> Errors { get; } = errors;
}
