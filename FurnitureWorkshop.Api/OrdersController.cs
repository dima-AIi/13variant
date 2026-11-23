using FurnitureWorkshop.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureWorkshop.Api;

[ApiController]
[Authorize]
[Route("api/orders")]
[Tags("Заказы")]
public sealed class OrdersController(IOrderRepository orders, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpGet]
    public Task<List<OrderInfo>> List([FromQuery] OrderStatus? status, [FromQuery] string? q, CancellationToken ct) =>
        orders.FindOrders(SessionTokens.User(User), status, q, ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderInfo>> Get(int id, CancellationToken ct) =>
        await orders.FindOrder(id, SessionTokens.User(User), ct) is { } order ? Ok(order) : NotFound();

    [HttpPost]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> Create(OrderInput input, CancellationToken ct)
    {
        int id = await orders.CreateOrder(input, ct);
        logger.LogInformation("Создан заказ {OrderId} пользователем {UserId}", id, SessionTokens.User(User).Id);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> Update(int id, OrderInput input, CancellationToken ct) =>
        await orders.UpdateOrder(id, input, ct) ? NoContent() : NotFound();

    [HttpPost("{id:int}/transition")]
    public async Task<IActionResult> Transition(int id, TransitionInput input, CancellationToken ct)
    {
        var actor = SessionTokens.User(User);
        if (!await orders.Transition(id, actor, input, ct))
            return NotFound();
        logger.LogInformation("Заказ {OrderId} переведён в {OrderStatus} пользователем {UserId}",
            id, input.Status, actor.Id);
        return NoContent();
    }

    [HttpGet("statistics")]
    public async Task<WorkshopStatistics> Statistics(CancellationToken ct) =>
        OrderRules.StatisticsFromMetrics(await orders.FindMetrics(SessionTokens.User(User), ct));
}
