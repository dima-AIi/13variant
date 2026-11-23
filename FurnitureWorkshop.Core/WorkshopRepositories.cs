namespace FurnitureWorkshop.Core;

public interface IOrderRepository
{
    Task<List<OrderInfo>> FindOrders(UserInfo user, OrderStatus? status, string? search, CancellationToken ct);
    Task<OrderInfo?> FindOrder(int id, UserInfo user, CancellationToken ct);
    Task<List<OrderMetrics>> FindMetrics(UserInfo user, CancellationToken ct);
    Task<int> CreateOrder(OrderInput input, CancellationToken ct);
    Task<bool> UpdateOrder(int id, OrderInput input, CancellationToken ct);
    Task<bool> Transition(int id, UserInfo user, TransitionInput input, CancellationToken ct);
}

public interface ICustomerRepository
{
    Task<List<CustomerInfo>> GetCustomers(CancellationToken ct);
    Task<int> CreateCustomer(CustomerInput input, CancellationToken ct);
    Task<bool> UpdateCustomer(int id, CustomerInput input, CancellationToken ct);
    Task<int> ImportCustomers(string csv, CancellationToken ct);
}

public interface IUserRepository
{
    Task<List<UserInfo>> GetUsers(bool assemblersOnly, CancellationToken ct);
    Task<WorkshopUser?> FindUser(string login, CancellationToken ct);
}
