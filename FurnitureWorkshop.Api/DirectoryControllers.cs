using FurnitureWorkshop.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureWorkshop.Api;

[ApiController]
[Authorize(Policy = "Staff")]
[Route("api/customers")]
[Tags("Заказчики")]
public sealed class CustomersController(ICustomerRepository customers) : ControllerBase
{
    [HttpGet]
    public Task<List<CustomerInfo>> List(CancellationToken ct) => customers.GetCustomers(ct);

    [HttpPost]
    public async Task<IActionResult> Create(CustomerInput input, CancellationToken ct)
    {
        int id = await customers.CreateCustomer(input, ct);
        return Created("/api/customers", new { id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CustomerInput input, CancellationToken ct) =>
        await customers.UpdateCustomer(id, input, ct) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/users")]
[Tags("Пользователи")]
public sealed class UsersController(IUserRepository users) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public Task<List<UserInfo>> List(CancellationToken ct) => users.GetUsers(false, ct);

    [HttpGet("assemblers")]
    [Authorize(Policy = "Staff")]
    public Task<List<UserInfo>> Assemblers(CancellationToken ct) => users.GetUsers(true, ct);
}
