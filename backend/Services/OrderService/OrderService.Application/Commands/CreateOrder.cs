using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using FluentValidation;
using MediatR;
using OrderService.Application.Abstractions;
using OrderService.Application.Dtos;
using OrderService.Domain.Entities;

namespace OrderService.Application.Commands;

public record CreateOrderCommand(string CustomerName, string CustomerEmail, string ContactNumber,
    string DeliveryAddress, string PaymentMethod, List<OrderItemRequest> Items) : IRequest<OrderDto>;

public class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public static readonly string[] PaymentMethods = ["CreditCard", "DebitCard", "CashOnDelivery"];

    public CreateOrderValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CustomerEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.ContactNumber).NotEmpty().MaximumLength(30).Matches(@"^[0-9+\-\s()]{5,30}$").WithMessage("Invalid contact number.");
        RuleFor(x => x.DeliveryAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.PaymentMethod).Must(m => PaymentMethods.Contains(m)).WithMessage("PaymentMethod must be CreditCard, DebitCard or CashOnDelivery.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductId).NotEmpty();
            i.RuleFor(x => x.Quantity).InclusiveBetween(1, 20);
        });
    }
}

public class CreateOrderHandler(IProductRepository products, IOrderRepository orders, IEventOutbox outbox,
    IUnitOfWork uow, IBusinessLogger log) : IRequestHandler<CreateOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var merged = cmd.Items.GroupBy(i => i.ProductId).Select(g => new OrderItemRequest(g.Key, g.Sum(x => x.Quantity))).ToList();
        var found = (await products.GetByIdsAsync(merged.Select(i => i.ProductId), ct)).ToDictionary(p => p.Id);

        var order = Order.Create(cmd.CustomerName.Trim(), cmd.CustomerEmail, cmd.ContactNumber.Trim(), cmd.DeliveryAddress.Trim(), cmd.PaymentMethod);
        foreach (var item in merged)
        {
            if (!found.TryGetValue(item.ProductId, out var product))
                throw new NotFoundException($"Product {item.ProductId} not found.");
            order.AddItem(product, item.Quantity); // validates + reserves stock
        }

        orders.Add(order);

        var correlation = CorrelationContext.Current;
        var created = new OrderCreatedEvent(order.Id, order.OrderNumber, order.CustomerName, order.CustomerEmail,
            order.TotalAmount, order.PaymentMethod, $"order-{order.Id}",
            order.Items.Select(i => new OrderItemPayload(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice)).ToList());
        outbox.Enqueue(EventTypes.OrderCreated, created, OutboxTargets.OrderCreatedQueue, OutboxTargets.OrderEventsTopic);
        outbox.Enqueue(EventTypes.CustomerNotification, new CustomerNotificationEvent(NotificationTypes.OrderConfirmation,
            order.CustomerEmail, order.CustomerName, order.OrderNumber, $"Order {order.OrderNumber} received",
            $"Hi {order.CustomerName}, we received your order {order.OrderNumber} totalling {order.TotalAmount:C}. Payment is being processed."),
            OutboxTargets.NotificationQueue);

        await uow.SaveChangesAsync(ct); // order + stock + outbox rows in ONE transaction

        log.Info("OrderCreated", $"Order successfully created. Total {order.TotalAmount}", order.OrderNumber);
        return order.ToDto();
    }
}
