using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PaymentService.Application.Commands;
using PaymentService.Application.Dtos;
using PaymentService.Application.Queries;

namespace PaymentService.API.Controllers;

public record ProcessPaymentRequest(Guid OrderId, string OrderNumber, string CustomerEmail, decimal Amount,
    string PaymentMethod, string IdempotencyKey);

/// <summary>Write endpoints are internal-only: they require the shared internal key (never exposed by the gateway).</summary>
public class InternalApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var expected = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Internal:ApiKey"];
        if (string.IsNullOrEmpty(expected) ||
            ctx.HttpContext.Request.Headers["X-Internal-Api-Key"] != expected)
        {
            ctx.Result = new UnauthorizedResult();
            return;
        }
        await next();
    }
}

[ApiController, Route("api/payments")]
public class PaymentsController(ISender mediator) : ControllerBase
{
    [HttpPost("process"), InternalApiKey]
    public async Task<ActionResult<PaymentDto>> Process(ProcessPaymentRequest r, CancellationToken ct) =>
        await mediator.Send(new ProcessPaymentCommand(r.OrderId, r.OrderNumber, r.CustomerEmail, r.Amount, r.PaymentMethod, r.IdempotencyKey), ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentDto>> Get(Guid id, CancellationToken ct) => await mediator.Send(new GetPaymentByIdQuery(id), ct);

    [HttpGet("order/{orderId:guid}")]
    public async Task<ActionResult<PaymentDto>> GetByOrder(Guid orderId, CancellationToken ct) => await mediator.Send(new GetPaymentByOrderIdQuery(orderId), ct);

    [HttpPost("{id:guid}/refund"), InternalApiKey]
    public async Task<ActionResult<PaymentDto>> Refund(Guid id, CancellationToken ct) => await mediator.Send(new RefundPaymentCommand(id), ct);
}
