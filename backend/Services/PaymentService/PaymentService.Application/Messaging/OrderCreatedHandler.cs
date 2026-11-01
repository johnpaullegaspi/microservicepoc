using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using MediatR;
using PaymentService.Application.Commands;

namespace PaymentService.Application.Messaging;

/// <summary>Saga step 1: OrderCreated (from SQS) -> process payment. Safe against duplicate deliveries (idempotency key).</summary>
public class OrderCreatedHandler(ISender mediator) : IMessageHandler<OrderCreatedEvent>
{
    public async Task HandleAsync(EventEnvelope<OrderCreatedEvent> m, CancellationToken ct)
    {
        var e = m.Payload;
        if (e.OrderId == Guid.Empty || e.TotalAmount <= 0 || string.IsNullOrWhiteSpace(e.IdempotencyKey))
            throw new ArgumentException($"Invalid OrderCreated event {m.EventId}"); // retried, then dead-lettered
        await mediator.Send(new ProcessPaymentCommand(e.OrderId, e.OrderNumber, e.CustomerEmail, e.TotalAmount,
            e.PaymentMethod, e.IdempotencyKey), ct);
    }
}
