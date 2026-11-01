using BuildingBlocks.Common;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using FluentValidation;
using OrderService.Application.Abstractions;
using OrderService.Application.Commands;
using OrderService.Application.Dtos;
using OrderService.Application.Queries;
using OrderService.Domain.Entities;
using Xunit;

namespace OrderService.UnitTests;

internal class FakeProducts : IProductRepository
{
    public readonly List<Product> Items = [];
    public Task<List<Product>> GetAllAsync(CancellationToken ct) => Task.FromResult(Items.ToList());
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
    public Task<List<Product>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult(Items.Where(p => ids.Contains(p.Id)).ToList());
}
internal class FakeOrders : IOrderRepository
{
    public readonly List<Order> Items = [];
    public void Add(Order o) => Items.Add(o);
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(o => o.Id == id));
    public Task<Order?> GetByNumberAsync(string n, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(o => o.OrderNumber == n));
    public Task<List<Order>> GetByCustomerAsync(string e, CancellationToken ct) => Task.FromResult(Items.Where(o => o.CustomerEmail == e).ToList());
}
internal class FakeOutbox : IEventOutbox
{
    public readonly List<(string Type, object Payload)> Events = [];
    public void Enqueue<T>(string eventType, T payload, params (OutboxDestinationKind Kind, string Destination)[] d) => Events.Add((eventType, payload!));
}
internal class FakeUow : IUnitOfWork
{
    public int Saves;
    public Task SaveChangesAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }
}
internal class NullLog : IBusinessLogger
{
    public void Info(string a, string b, string? c = null, string? d = null) { }
    public void Warn(string a, string b, string? c = null, string? d = null) { }
    public void Error(string a, string b, Exception? e = null, string? c = null, string? d = null) { }
}

public class OrderTests
{
    private readonly FakeProducts _products = new();
    private readonly FakeOrders _orders = new();
    private readonly FakeOutbox _outbox = new();
    private readonly FakeUow _uow = new();
    private readonly Product _shoe = new() { ProductName = "Nike Air Max", SKU = "S1", Price = 100m, StockQuantity = 5, Description = "d", ImageUrl = "i" };

    public OrderTests() => _products.Items.Add(_shoe);

    private CreateOrderCommand Cmd(int qty = 2, string email = "jane@example.com") => new("Jane", email, "+639171234567", "1 Main St",
        "CreditCard", [new OrderItemRequest(_shoe.Id, qty)]);

    private Task<OrderDto> Create(CreateOrderCommand c) =>
        new CreateOrderHandler(_products, _orders, _outbox, _uow, new NullLog()).Handle(c, default);

    [Fact]
    public async Task CreateOrder_Succeeds_ReservesStock_AndWritesOutbox()
    {
        var dto = await Create(Cmd(2));
        Assert.Equal(200m, dto.TotalAmount);
        Assert.Equal("Pending", dto.OrderStatus);
        Assert.Equal(3, _shoe.StockQuantity);
        Assert.Single(_orders.Items);
        Assert.Equal(1, _uow.Saves); // order + outbox saved in one unit of work
        Assert.Contains(_outbox.Events, e => e.Type == "OrderCreated");
        Assert.Contains(_outbox.Events, e => e.Type == "CustomerNotification");
    }

    [Fact]
    public void CreateOrder_InvalidRequest_IsRejectedByValidator()
    {
        var bad = new CreateOrderCommand("", "not-an-email", "x", "", "Bitcoin", []);
        var result = new CreateOrderValidator().Validate(bad);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 5);
    }

    [Fact]
    public async Task CreateOrder_InsufficientStock_Throws_AndDoesNotSave()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Create(Cmd(6)));
        Assert.Equal(0, _uow.Saves);
        Assert.Empty(_orders.Items);
    }

    [Fact]
    public async Task CreateOrder_UnknownProduct_ThrowsNotFound()
    {
        var c = Cmd() with { Items = [new OrderItemRequest(Guid.NewGuid(), 1)] };
        await Assert.ThrowsAsync<NotFoundException>(() => Create(c));
    }

    [Fact]
    public async Task GetOrder_ByIdAndNumber_ReturnsOrder()
    {
        var created = await Create(Cmd());
        var byId = await new GetOrderByIdHandler(_orders).Handle(new GetOrderByIdQuery(created.Id), default);
        var byNo = await new GetOrderByNumberHandler(_orders).Handle(new GetOrderByNumberQuery(created.OrderNumber), default);
        Assert.Equal(created.OrderNumber, byId.OrderNumber);
        Assert.Equal(created.Id, byNo.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => new GetOrderByIdHandler(_orders).Handle(new GetOrderByIdQuery(Guid.NewGuid()), default));
    }

    [Fact]
    public async Task CancelOrder_Eligible_CancelsAndReleasesStock()
    {
        var created = await Create(Cmd(2));
        var dto = await new CancelOrderHandler(_orders, _products, _outbox, _uow, new NullLog())
            .Handle(new CancelOrderCommand(created.Id, "changed mind"), default);
        Assert.Equal("Cancelled", dto.OrderStatus);
        Assert.Equal(5, _shoe.StockQuantity);
        Assert.Contains(_outbox.Events, e => e.Type == "OrderCancelled");
    }

    [Fact]
    public async Task CancelOrder_Confirmed_IsRejected()
    {
        var created = await Create(Cmd());
        var order = _orders.Items.Single();
        order.ApplyPaymentResult(OrderPaymentStatus.Successful, "TXN-1", null);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new CancelOrderHandler(_orders, _products, _outbox, _uow, new NullLog()).Handle(new CancelOrderCommand(created.Id, null), default));
    }

    [Fact]
    public async Task UpdatePaymentStatus_Success_ConfirmsOrder_AndIsIdempotent()
    {
        var created = await Create(Cmd());
        var h = new UpdatePaymentStatusHandler(_orders, _products, _outbox, _uow, new NullLog());
        var first = await h.Handle(new UpdatePaymentStatusCommand(created.Id, OrderPaymentStatus.Successful, "TXN-9"), default);
        var notifications = _outbox.Events.Count(e => e.Type == "CustomerNotification");
        var second = await h.Handle(new UpdatePaymentStatusCommand(created.Id, OrderPaymentStatus.Successful, "TXN-9"), default);
        Assert.Equal("Confirmed", first.OrderStatus);
        Assert.Equal("TXN-9", second.TransactionReference);
        Assert.Equal(notifications, _outbox.Events.Count(e => e.Type == "CustomerNotification")); // no duplicate notification
    }

    [Fact]
    public async Task UpdatePaymentStatus_Failure_SetsPaymentFailed_AndReleasesStock()
    {
        var created = await Create(Cmd(2));
        var dto = await new UpdatePaymentStatusHandler(_orders, _products, _outbox, _uow, new NullLog())
            .Handle(new UpdatePaymentStatusCommand(created.Id, OrderPaymentStatus.Failed, null, "declined"), default);
        Assert.Equal("PaymentFailed", dto.OrderStatus);
        Assert.Equal(5, _shoe.StockQuantity);
    }
}
