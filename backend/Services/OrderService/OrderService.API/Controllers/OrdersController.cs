using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Commands;
using OrderService.Application.Dtos;
using OrderService.Application.Queries;

namespace OrderService.API.Controllers;

public record CreateOrderRequest(string CustomerName, string CustomerEmail, string ContactNumber,
    string DeliveryAddress, string PaymentMethod, List<OrderItemRequest> Items);
public record CancelOrderRequest(string? Reason);

[ApiController, Route("api/orders")]
public class OrdersController(ISender mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest r, CancellationToken ct)
    {
        var order = await mediator.Send(new CreateOrderCommand(r.CustomerName, r.CustomerEmail, r.ContactNumber,
            r.DeliveryAddress, r.PaymentMethod, r.Items ?? []), ct);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken ct) => await mediator.Send(new GetOrderByIdQuery(id), ct);

    [HttpGet("number/{orderNumber}")]
    public async Task<ActionResult<OrderDto>> GetByNumber(string orderNumber, CancellationToken ct) => await mediator.Send(new GetOrderByNumberQuery(orderNumber), ct);

    [HttpGet("customer/{email}")]
    public async Task<ActionResult<List<OrderDto>>> GetByCustomer(string email, CancellationToken ct) => await mediator.Send(new GetCustomerOrdersQuery(email), ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id, [FromBody] CancelOrderRequest? r, CancellationToken ct) =>
        await mediator.Send(new CancelOrderCommand(id, r?.Reason), ct);
}
