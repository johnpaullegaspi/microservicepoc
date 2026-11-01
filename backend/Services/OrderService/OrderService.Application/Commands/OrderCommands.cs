using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using MediatR;
using OrderService.Application.Abstractions;
using OrderService.Application.Dtos;
using OrderService.Domain.Entities;

namespace OrderService.Application.Commands;

// ---------- Cancel ----------
public record CancelOrderCommand(Guid OrderId, string? Reason) : IRequest<OrderDto>;

public class CancelOrderHandler(IOrderRepository orders, IProductRepository products, IEventOutbox outbox,
    IUnitOfWork uow, IBusinessLogger log) : IRequestHandler<CancelOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct) ?? throw new NotFoundException("Order not found.");
        order.Cancel(); // throws BusinessRuleException if not eligible

        var items = await products.GetByIdsAsync(order.Items.Select(i => i.ProductId), ct);
        foreach (var i in order.Items) items.FirstOrDefault(p => p.Id == i.ProductId)?.Release(i.Quantity);

        var reason = cmd.Reason ?? "Cancelled by customer";
        outbox.Enqueue(EventTypes.OrderCancelled, new OrderCancelledEvent(order.Id, order.OrderNumber, order.CustomerEmail, reason),
            OutboxTargets.OrderEventsTopic);
        outbox.Enqueue(EventTypes.CustomerNotification, new CustomerNotificationEvent(NotificationTypes.OrderCancellation,
            order.CustomerEmail, order.CustomerName, order.OrderNumber, $"Order {order.OrderNumber} cancelled",
            $"Hi {order.CustomerName}, your order {order.OrderNumber} has been cancelled."), OutboxTargets.NotificationQueue);
        await uow.SaveChangesAsync(ct);

        log.Info("OrderCancelled", $"Order cancelled. Reason: {reason}", order.OrderNumber);
        return order.ToDto();
    }
}

// ---------- Update order status ----------
public record UpdateOrderStatusCommand(Guid OrderId, OrderStatus Status) : IRequest<OrderDto>;

public class UpdateOrderStatusHandler(IOrderRepository orders, IUnitOfWork uow, IBusinessLogger log)
    : IRequestHandler<UpdateOrderStatusCommand, OrderDto>
{
    public async Task<OrderDto> Handle(UpdateOrderStatusCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct) ?? throw new NotFoundException("Order not found.");
        order.SetStatus(cmd.Status);
        await uow.SaveChangesAsync(ct);
        log.Info("OrderStatusUpdated", $"Order status set to {cmd.Status}", order.OrderNumber);
        return order.ToDto();
    }
}

// ---------- Update payment status (driven by payment results) ----------
public record UpdatePaymentStatusCommand(Guid OrderId, OrderPaymentStatus PaymentStatus,
    string? TransactionReference = null, string? FailureReason = null) : IRequest<OrderDto>;

public class UpdatePaymentStatusHandler(IOrderRepository orders, IProductRepository products, IEventOutbox outbox,
    IUnitOfWork uow, IBusinessLogger log) : IRequestHandler<UpdatePaymentStatusCommand, OrderDto>
{
    public async Task<OrderDto> Handle(UpdatePaymentStatusCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct) ?? throw new NotFoundException("Order not found.");

        // Idempotent: replaying the same result is a no-op
        if (order.PaymentStatus == cmd.PaymentStatus && order.TransactionReference == (cmd.TransactionReference ?? order.TransactionReference))
            return order.ToDto();

        var wasFailed = order.OrderStatus == OrderStatus.PaymentFailed;
        order.ApplyPaymentResult(cmd.PaymentStatus, cmd.TransactionReference, cmd.FailureReason);

        if (cmd.PaymentStatus == OrderPaymentStatus.Successful)
            Notify(outbox, order, NotificationTypes.PaymentSuccess, $"Payment received for {order.OrderNumber}",
                $"Hi {order.CustomerName}, payment of {order.TotalAmount:C} was successful (ref {order.TransactionReference}). Your order is confirmed!");
        else if (cmd.PaymentStatus == OrderPaymentStatus.Failed)
        {
            // payment failed -> release reserved stock so others can buy
            var prods = await products.GetByIdsAsync(order.Items.Select(i => i.ProductId), ct);
            if (!wasFailed) foreach (var i in order.Items) prods.FirstOrDefault(p => p.Id == i.ProductId)?.Release(i.Quantity);
            Notify(outbox, order, NotificationTypes.PaymentFailure, $"Payment failed for {order.OrderNumber}",
                $"Hi {order.CustomerName}, your payment could not be completed: {cmd.FailureReason}.");
        }
        await uow.SaveChangesAsync(ct);

        if (cmd.PaymentStatus == OrderPaymentStatus.Successful)
            log.Info("PaymentSucceeded", $"Order confirmed. Ref {order.TransactionReference}", order.OrderNumber, order.TransactionReference);
        else if (cmd.PaymentStatus == OrderPaymentStatus.Failed)
            log.Warn("PaymentFailed", $"Payment failed: {cmd.FailureReason}", order.OrderNumber);
        return order.ToDto();
    }

    private static void Notify(IEventOutbox outbox, Order o, string type, string subject, string body) =>
        outbox.Enqueue(EventTypes.CustomerNotification,
            new CustomerNotificationEvent(type, o.CustomerEmail, o.CustomerName, o.OrderNumber, subject, body),
            OutboxTargets.NotificationQueue);
}
