using BuildingBlocks.Messaging;
using PaymentService.Domain.Entities;

namespace PaymentService.Application.Abstractions;

public interface IPaymentRepository
{
    void Add(Payment payment);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken ct);
    Task<Payment?> GetByIdempotencyKeyAsync(string key, CancellationToken ct);
}

public interface IEventOutbox
{
    void Enqueue<T>(string eventType, T payload, params (OutboxDestinationKind Kind, string Destination)[] destinations);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
    /// <summary>Detach tracked state after a unique-constraint race so the row can be re-read.</summary>
    void ClearTracked();
}

public record GatewayResult(bool Success, string? TransactionReference, string? FailureReason);

public interface IPaymentGateway
{
    Task<GatewayResult> ChargeAsync(Payment payment, CancellationToken ct);
}
