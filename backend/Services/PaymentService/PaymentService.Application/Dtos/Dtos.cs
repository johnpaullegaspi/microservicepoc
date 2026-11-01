using PaymentService.Domain.Entities;

namespace PaymentService.Application.Dtos;

public record PaymentDto(Guid Id, Guid OrderId, string OrderNumber, string CustomerEmail, decimal Amount,
    string PaymentMethod, string? TransactionReference, string IdempotencyKey, string PaymentStatus,
    string? FailureReason, DateTime CreatedAt, DateTime UpdatedAt);

public static class Mappings
{
    public static PaymentDto ToDto(this Payment p) => new(p.Id, p.OrderId, p.OrderNumber, p.CustomerEmail, p.Amount,
        p.PaymentMethod, p.TransactionReference, p.IdempotencyKey, p.PaymentStatus.ToString(), p.FailureReason, p.CreatedAt, p.UpdatedAt);
}
