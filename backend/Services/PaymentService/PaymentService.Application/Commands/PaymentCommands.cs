using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using FluentValidation;
using MediatR;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Dtos;
using PaymentService.Domain.Entities;

namespace PaymentService.Application.Commands;

internal static class Targets
{
    public static readonly (OutboxDestinationKind, string) PaymentTopic = (OutboxDestinationKind.Topic, TopicNames.PaymentEvents);
}

// ---------------- Process ----------------
public record ProcessPaymentCommand(Guid OrderId, string OrderNumber, string CustomerEmail, decimal Amount,
    string PaymentMethod, string IdempotencyKey) : IRequest<PaymentDto>;

public class ProcessPaymentValidator : AbstractValidator<ProcessPaymentCommand>
{
    public ProcessPaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.OrderNumber).NotEmpty().MaximumLength(40);
        RuleFor(x => x.CustomerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.PaymentMethod).NotEmpty().MaximumLength(30);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}

public class ProcessPaymentHandler(IPaymentRepository payments, IPaymentGateway gateway, IEventOutbox outbox,
    IUnitOfWork uow, IBusinessLogger log) : IRequestHandler<ProcessPaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(ProcessPaymentCommand cmd, CancellationToken ct)
    {
        // 1. Idempotency: same key => same payment, never charged twice
        var payment = await payments.GetByIdempotencyKeyAsync(cmd.IdempotencyKey, ct);
        if (payment is { IsFinal: true }) return payment.ToDto();

        if (payment is null)
        {
            payment = Payment.Create(cmd.OrderId, cmd.OrderNumber, cmd.CustomerEmail.ToLowerInvariant(), cmd.Amount,
                cmd.PaymentMethod, cmd.IdempotencyKey);
            payment.StartProcessing();
            payments.Add(payment);
            try { await uow.SaveChangesAsync(ct); }
            catch (Exception) // unique index on IdempotencyKey: a concurrent delivery won the race
            {
                uow.ClearTracked();
                var existing = await payments.GetByIdempotencyKeyAsync(cmd.IdempotencyKey, ct);
                if (existing is not null) return existing.ToDto();
                throw;
            }
            log.Info("PaymentInitiated", $"Payment initiated for {cmd.Amount} via {cmd.PaymentMethod}", cmd.OrderNumber, payment.Id.ToString());
        }

        // 2. Charge via simulated gateway (no card data is ever handled)
        var result = await gateway.ChargeAsync(payment, ct);

        // 3. Persist result + outbox event atomically
        if (result.Success)
        {
            payment.MarkSuccessful(result.TransactionReference!);
            outbox.Enqueue(EventTypes.PaymentSucceeded, new PaymentSucceededEvent(payment.Id, payment.OrderId,
                payment.OrderNumber, payment.CustomerEmail, payment.Amount, payment.TransactionReference!), Targets.PaymentTopic);
        }
        else
        {
            payment.MarkFailed(result.FailureReason ?? "Payment declined");
            outbox.Enqueue(EventTypes.PaymentFailed, new PaymentFailedEvent(payment.Id, payment.OrderId,
                payment.OrderNumber, payment.CustomerEmail, payment.Amount, payment.FailureReason!), Targets.PaymentTopic);
        }
        await uow.SaveChangesAsync(ct);

        if (result.Success) log.Info("PaymentSucceeded", $"Payment successful. Ref {payment.TransactionReference}", payment.OrderNumber, payment.Id.ToString());
        else log.Warn("PaymentFailed", $"Payment failed: {payment.FailureReason}", payment.OrderNumber, payment.Id.ToString());
        return payment.ToDto();
    }
}

// ---------------- Refund ----------------
public record RefundPaymentCommand(Guid PaymentId) : IRequest<PaymentDto>;

public class RefundPaymentHandler(IPaymentRepository payments, IEventOutbox outbox, IUnitOfWork uow, IBusinessLogger log)
    : IRequestHandler<RefundPaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(RefundPaymentCommand cmd, CancellationToken ct)
    {
        var p = await payments.GetByIdAsync(cmd.PaymentId, ct) ?? throw new NotFoundException("Payment not found.");
        if (p.PaymentStatus == PaymentStatus.Refunded) return p.ToDto(); // idempotent
        p.Refund(); // mock refund
        outbox.Enqueue(EventTypes.RefundProcessed, new RefundProcessedEvent(p.Id, p.OrderId, p.OrderNumber, p.CustomerEmail, p.Amount), Targets.PaymentTopic);
        await uow.SaveChangesAsync(ct);
        log.Info("RefundProcessed", $"Refund of {p.Amount} processed", p.OrderNumber, p.Id.ToString());
        return p.ToDto();
    }
}

// ---------------- Update status ----------------
public record UpdatePaymentStatusCommand(Guid PaymentId, PaymentStatus Status, string? TransactionReference = null,
    string? FailureReason = null) : IRequest<PaymentDto>;

public class UpdatePaymentStatusHandler(IPaymentRepository payments, IUnitOfWork uow, IBusinessLogger log)
    : IRequestHandler<UpdatePaymentStatusCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(UpdatePaymentStatusCommand cmd, CancellationToken ct)
    {
        var p = await payments.GetByIdAsync(cmd.PaymentId, ct) ?? throw new NotFoundException("Payment not found.");
        p.SetStatus(cmd.Status, cmd.TransactionReference, cmd.FailureReason);
        await uow.SaveChangesAsync(ct);
        log.Info("PaymentStatusUpdated", $"Payment status set to {cmd.Status}", p.OrderNumber, p.Id.ToString());
        return p.ToDto();
    }
}
