using BuildingBlocks.Common;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.Options;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Commands;
using PaymentService.Application.Queries;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;
using Xunit;

namespace PaymentService.UnitTests;

internal class FakePayments : IPaymentRepository
{
    public readonly List<Payment> Items = [];
    public void Add(Payment p) => Items.Add(p);
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
    public Task<Payment?> GetByOrderIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(p => p.OrderId == id));
    public Task<Payment?> GetByIdempotencyKeyAsync(string k, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(p => p.IdempotencyKey == k));
}
internal class FakeOutbox : IEventOutbox
{
    public readonly List<string> Events = [];
    public void Enqueue<T>(string t, T p, params (OutboxDestinationKind Kind, string Destination)[] d) => Events.Add(t);
}
internal class FakeUow : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    public void ClearTracked() { }
}
internal class CountingGateway(IPaymentGateway inner) : IPaymentGateway
{
    public int Calls;
    public Task<GatewayResult> ChargeAsync(Payment p, CancellationToken ct) { Calls++; return inner.ChargeAsync(p, ct); }
}
internal class NullLog : IBusinessLogger
{
    public void Info(string a, string b, string? c = null, string? d = null) { }
    public void Warn(string a, string b, string? c = null, string? d = null) { }
    public void Error(string a, string b, Exception? e = null, string? c = null, string? d = null) { }
}

public class PaymentTests
{
    private readonly FakePayments _repo = new();
    private readonly FakeOutbox _outbox = new();
    private readonly CountingGateway _gateway = new(new SimulatedPaymentGateway(Options.Create(new PaymentGatewayOptions { DelayMs = 0 })));

    private ProcessPaymentHandler Handler() => new(_repo, _gateway, _outbox, new FakeUow(), new NullLog());
    private static ProcessPaymentCommand Cmd(string email = "jane@example.com", string key = "order-1") =>
        new(Guid.NewGuid(), "ORD-1", email, 99.5m, "CreditCard", key);

    [Fact]
    public async Task ProcessPayment_Success_StoresReference_AndPublishesSucceeded()
    {
        var dto = await Handler().Handle(Cmd(), default);
        Assert.Equal("Successful", dto.PaymentStatus);
        Assert.StartsWith("TXN-", dto.TransactionReference);
        Assert.Contains("PaymentSucceeded", _outbox.Events);
    }

    [Fact]
    public async Task ProcessPayment_FailureScenario_PublishesFailed()
    {
        var dto = await Handler().Handle(Cmd("customer.fail@example.com"), default);
        Assert.Equal("Failed", dto.PaymentStatus);
        Assert.NotNull(dto.FailureReason);
        Assert.Contains("PaymentFailed", _outbox.Events);
    }

    [Fact]
    public async Task ProcessPayment_DuplicateDelivery_ChargesOnlyOnce()
    {
        var c = Cmd();
        var first = await Handler().Handle(c, default);
        var second = await Handler().Handle(c, default);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(_repo.Items);
        Assert.Equal(1, _gateway.Calls);
        Assert.Single(_outbox.Events);
    }

    [Fact]
    public async Task GetPayment_ByIdAndOrder_ReturnsPayment()
    {
        var c = Cmd();
        var created = await Handler().Handle(c, default);
        Assert.Equal(created.Id, (await new GetPaymentByIdHandler(_repo).Handle(new GetPaymentByIdQuery(created.Id), default)).Id);
        Assert.Equal(created.Id, (await new GetPaymentByOrderIdHandler(_repo).Handle(new GetPaymentByOrderIdQuery(c.OrderId), default)).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => new GetPaymentByIdHandler(_repo).Handle(new GetPaymentByIdQuery(Guid.NewGuid()), default));
    }

    [Fact]
    public async Task Refund_Successful_BecomesRefunded_AndIsIdempotent()
    {
        var created = await Handler().Handle(Cmd(), default);
        var h = new RefundPaymentHandler(_repo, _outbox, new FakeUow(), new NullLog());
        var r1 = await h.Handle(new RefundPaymentCommand(created.Id), default);
        var r2 = await h.Handle(new RefundPaymentCommand(created.Id), default);
        Assert.Equal("Refunded", r1.PaymentStatus);
        Assert.Equal("Refunded", r2.PaymentStatus);
        Assert.Single(_outbox.Events, e => e == "RefundProcessed");
    }

    [Fact]
    public async Task Refund_FailedPayment_IsRejected()
    {
        var failed = await Handler().Handle(Cmd("x.fail@example.com", "order-2"), default);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new RefundPaymentHandler(_repo, _outbox, new FakeUow(), new NullLog()).Handle(new RefundPaymentCommand(failed.Id), default));
    }
}
