using BuildingBlocks.Common;

namespace PaymentService.Domain.Entities;

public enum PaymentStatus { Pending = 0, Processing = 1, Successful = 2, Failed = 3, Refunded = 4 }

public class Payment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public string OrderNumber { get; private set; } = default!;
    public string CustomerEmail { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string PaymentMethod { get; private set; } = default!;
    public string? TransactionReference { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.Pending;
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private Payment() { }

    public static Payment Create(Guid orderId, string orderNumber, string email, decimal amount, string method, string idempotencyKey)
    {
        if (amount <= 0) throw new BusinessRuleException("Amount must be greater than zero.");
        return new Payment { OrderId = orderId, OrderNumber = orderNumber, CustomerEmail = email, Amount = amount,
            PaymentMethod = method, IdempotencyKey = idempotencyKey };
    }

    public void StartProcessing() { PaymentStatus = PaymentStatus.Processing; Touch(); }

    public void MarkSuccessful(string reference)
    {
        if (PaymentStatus is PaymentStatus.Refunded) throw new BusinessRuleException("Payment already refunded.");
        PaymentStatus = PaymentStatus.Successful; TransactionReference = reference; FailureReason = null; Touch();
    }

    public void MarkFailed(string reason)
    {
        if (PaymentStatus is PaymentStatus.Successful or PaymentStatus.Refunded)
            throw new BusinessRuleException("Cannot fail a completed payment.");
        PaymentStatus = PaymentStatus.Failed; FailureReason = reason; Touch();
    }

    public void Refund()
    {
        if (PaymentStatus != PaymentStatus.Successful)
            throw new BusinessRuleException($"Only successful payments can be refunded (current: {PaymentStatus}).");
        PaymentStatus = PaymentStatus.Refunded; Touch();
    }

    public void SetStatus(PaymentStatus status, string? reference, string? reason)
    {
        PaymentStatus = status; TransactionReference = reference ?? TransactionReference; FailureReason = reason; Touch();
    }

    public bool IsFinal => PaymentStatus is PaymentStatus.Successful or PaymentStatus.Failed or PaymentStatus.Refunded;
    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
