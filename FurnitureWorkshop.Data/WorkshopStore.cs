using System.Linq.Expressions;
using FurnitureWorkshop.Core;
using Microsoft.EntityFrameworkCore;

namespace FurnitureWorkshop.Data;

public sealed class WorkshopStore(WorkshopDb db, TimeProvider time) : IOrderRepository, ICustomerRepository, IUserRepository
{
    private static readonly Expression<Func<FurnitureOrder, OrderInfo>> OrderProjection = row =>
        new OrderInfo(row.Id, row.CustomerId, row.Customer.Name, row.Customer.Phone, row.Title,
            row.Address, row.Notes, row.AssemblerId, row.Assembler == null ? null : row.Assembler.DisplayName,
            row.Status, row.Amount, row.DeliveryDate, row.CancellationReason, row.CreatedAt,
            row.CompletedAt, row.Version);

    public Task<List<OrderInfo>> FindOrders(UserInfo user, OrderStatus? status, string? search, CancellationToken ct)
    {
        var query = VisibleOrders(user).AsNoTracking();
        if (status.HasValue)
            query = query.Where(row => row.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            string uppercase = term.ToUpperInvariant();
            bool byId = int.TryParse(term.TrimStart('#'), out int orderId);
            // SQLite's built-in NOCASE collation does not fold Cyrillic.
            query = query.Where(row => (byId && row.Id == orderId) ||
                WorkshopContains(row.Title, uppercase) || WorkshopContains(row.Address, uppercase) ||
                WorkshopContains(row.Customer.Name, uppercase) || row.Customer.Phone.Contains(term));
        }

        return query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Select(OrderProjection).ToListAsync(ct);
    }

    [DbFunction("workshop_contains", IsBuiltIn = true)]
    public static bool WorkshopContains(string value, string uppercase) =>
        throw new InvalidOperationException("Функция выполняется в SQLite.");

    public Task<OrderInfo?> FindOrder(int id, UserInfo user, CancellationToken ct) =>
        VisibleOrders(user).AsNoTracking().Where(row => row.Id == id)
            .Select(OrderProjection).SingleOrDefaultAsync(ct);

    public Task<List<OrderMetrics>> FindMetrics(UserInfo user, CancellationToken ct) =>
        VisibleOrders(user).AsNoTracking()
            .Select(row => new OrderMetrics(row.Status, row.Amount, row.CreatedAt, row.CompletedAt)).ToListAsync(ct);

    public Task<List<CustomerInfo>> GetCustomers(CancellationToken ct) => db.Customers.AsNoTracking()
        .OrderBy(customer => customer.Name).ThenBy(customer => customer.Id)
        .Select(customer => new CustomerInfo(customer.Id, customer.Name, customer.Phone, customer.Email)).ToListAsync(ct);

    public Task<List<UserInfo>> GetUsers(bool assemblersOnly, CancellationToken ct) =>
        db.Users.AsNoTracking().Where(user => !assemblersOnly || user.Role == Roles.Assembler)
            .OrderBy(user => user.Id)
            .Select(user => new UserInfo(user.Id, user.Login, user.DisplayName, user.Role)).ToListAsync(ct);

    public Task<WorkshopUser?> FindUser(string login, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Login == login, ct);

    public async Task<int> CreateCustomer(CustomerInput input, CancellationToken ct)
    {
        var customer = NewCustomer(InputRules.Customer(input));
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer.Id;
    }

    public async Task<bool> UpdateCustomer(int id, CustomerInput input, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null)
            return false;

        var values = InputRules.Customer(input);
        customer.Name = values.Name;
        customer.Phone = values.Phone;
        customer.Email = values.Email!;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ImportCustomers(string csv, CancellationToken ct)
    {
        var values = WorkshopFiles.ReadCustomers(csv);
        db.Customers.AddRange(values.Select(NewCustomer));
        await db.SaveChangesAsync(ct);
        return values.Count;
    }

    public async Task<int> CreateOrder(OrderInput input, CancellationToken ct)
    {
        var order = new FurnitureOrder { CreatedAt = time.GetUtcNow().UtcDateTime, Version = 1 };
        await SetDetails(order, input, ct);
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order.Id;
    }

    public async Task<bool> UpdateOrder(int id, OrderInput input, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync([id], ct);
        if (order is null)
            return false;

        CheckVersion(order, input.Version);
        if (OrderRules.IsFinal(order.Status))
            throw new RuleException("Завершённый заказ закрыт для редактирования.");

        await SetDetails(order, input, ct);
        order.Version++;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> Transition(int id, UserInfo user, TransitionInput input, CancellationToken ct)
    {
        var order = await VisibleOrders(user).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (order is null)
            return false;

        CheckVersion(order, input.Version);
        OrderRules.Transition(order, input.Status, input.Reason, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<FurnitureOrder> VisibleOrders(UserInfo user) =>
        user.Role == Roles.Assembler ? db.Orders.Where(row => row.AssemblerId == user.Id) : db.Orders;

    private static void CheckVersion(FurnitureOrder order, int? version)
    {
        if (version is null)
            throw new RuleException("Укажите версию заказа.");
        if (order.Version != version)
            throw new OrderConflictException();
    }

    private Customer NewCustomer(CustomerInput input) =>
        new() { Name = input.Name, Phone = input.Phone, Email = input.Email!, RegisteredAt = time.GetUtcNow().UtcDateTime };

    private async Task SetDetails(FurnitureOrder order, OrderInput input, CancellationToken ct)
    {
        order.Title = InputRules.Required(input.Title, "Изделие", 180);
        order.Address = InputRules.Required(input.Address, "Адрес установки", 220);
        order.Notes = InputRules.Optional(input.Notes, "Примечания и размеры", 2000);
        InputRules.CheckAmount(input.Amount);

        if (!await db.Customers.AnyAsync(customer => customer.Id == input.CustomerId, ct))
            throw new RuleException("Выберите существующего заказчика.");

        if (input.AssemblerId is { } assemblerId &&
            !await db.Users.AnyAsync(user => user.Id == assemblerId && user.Role == Roles.Assembler, ct))
            throw new RuleException("Выберите пользователя с ролью сборщика.");

        if (order.Status is OrderStatus.Manufacturing or OrderStatus.Delivery && input.AssemblerId is null)
            throw new RuleException("У заказа на изготовлении или доставке должен быть сборщик.");

        order.CustomerId = input.CustomerId;
        order.AssemblerId = input.AssemblerId;
        order.Amount = input.Amount;
        order.DeliveryDate = input.DeliveryDate;
    }
}
