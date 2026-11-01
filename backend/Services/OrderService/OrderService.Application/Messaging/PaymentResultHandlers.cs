using System.Text.Json;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using MediatR;
using OrderService.Application.Commands;
using OrderService.Domain.Entities;

namespace OrderService.Application.Messaging;

/// <summary>
/// Consumes payment-result-queue (fed by SNS payment-events-topic). One queue carries two event types,
/// so the payload is read as JsonElement and dispatched on EventType. Saga step: payment result -> order state.
/// </summary>
public class PaymentResultHandler(ISender mediator) : IMessageHandler<JsonElement>
{
    public async Task HandleAsync(EventEnvelope<JsonElement> m, CancellationToken ct)
    {
        switch (m.EventType)
        {
            case EventTypes.PaymentSucceeded:
                var ok = m.Payload.Deserialize<PaymentSucceededEvent>(MessageSerializer.Options)!;
                await mediator.Send(new UpdatePaymentStatusCommand(ok.OrderId, OrderPaymentStatus.Successful, ok.TransactionReference), ct);
                break;
            case EventTypes.PaymentFailed:
                var failed = m.Payload.Deserialize<PaymentFailedEvent>(MessageSerializer.Options)!;
                await mediator.Send(new UpdatePaymentStatusCommand(failed.OrderId, OrderPaymentStatus.Failed, null, failed.FailureReason), ct);
                break;
            default:
                // unknown/other event types (e.g. RefundProcessed) are acknowledged and ignored
                break;
        }
    }
}
