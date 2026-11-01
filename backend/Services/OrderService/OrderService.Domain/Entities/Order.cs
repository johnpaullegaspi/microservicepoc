using BuildingBlocks.Common;

namespace OrderService.Domain.Entities;

public enum OrderStatus { Pending = 0, Processing = 1, Confirmed = 2, PaymentFailed = 3, Cancelled = 4 }
public enum OrderPaymentStatus { Pending = 0, Processing = 1, Successful = 2, Failed = 3, Refunded = 4 }

public class Order
{
    private readonly List<OrderItem> _items = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string OrderNumber { get; private set; } = default!;
    public string CustomerName { get; private set; } = default!;
    public string CustomerEmail { get; private set; } = default!;
    public string ContactNumber { get; private set; } = default!;
    public string DeliveryAddress { get; private set; } = default!;
    public string PaymentMethod { get; private set; } = default!;
    public decimal TotalAmount { get; private set; }
    public OrderStatus OrderStatus { get; private set; } = OrderStatus.Pending;
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.Pending;
    public string? TransactionReference { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public IReadOnlyCollection<OrderItem> Items => _items;

    private Order() { }

    public static Order Create(string name, string email, string phone, string address, string paymentMethod)
    {
        var o = new Order
        {
            CustomerName = name, CustomerEmail = email.Trim().ToLowerInvariant(), ContactNumber = phone,
            DeliveryAddress = address, PaymentMethod = paymentMethod
        };
        o.OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        return o;
    }

    public void AddItem(Product product, int quantity)
    {
        product.Reserve(quantity);
        var existing = _items.FirstOrDefault(i => i.ProductId == product.Id);
        if (existing is not null) existing.AddQuantity(quantity);
        else _items.Add(new OrderItem(Id, product.Id, product.ProductName, quantity, product.Price));
        TotalAmount = _items.Sum(i => i.Subtotal);
    }

    public bool CanBeCancelled => OrderStatus is OrderStatus.Pending or OrderStatus.PaymentFailed;

    public void Cancel()
    {
        if (!CanBeCancelled)
            throw new BusinessRuleException($"Order {OrderNumber} cannot be cancelled in status {OrderStatus}.");
        OrderStatus = OrderStatus.Cancelled;
        Touch();
    }

    public void SetStatus(OrderStatus status)
    {
        if (OrderStatus == OrderStatus.Cancelled && status != OrderStatus.Cancelled)
            throw new BusinessRuleException("A cancelled order cannot change status.");
        OrderStatus = status;
        Touch();
    }

    public void ApplyPaymentResult(OrderPaymentStatus payment, string? transactionReference, string? failureReason)
    {
        PaymentStatus = payment;
        TransactionReference = transactionReference ?? TransactionReference;
        FailureReason = failureReason;
        if (OrderStatus != OrderStatus.Cancelled)
            OrderStatus = payment switch
            {
                OrderPaymentStatus.Successful => OrderStatus.Confirmed,
                OrderPaymentStatus.Failed => OrderStatus.PaymentFailed,
                OrderPaymentStatus.Processing => OrderStatus.Processing,
                _ => OrderStatus
            };
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}

public class OrderItem
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Subtotal { get; private set; }

    private OrderItem() { }
    public OrderItem(Guid orderId, Guid productId, string name, int qty, decimal unitPrice)
    {
        OrderId = orderId; ProductId = productId; ProductName = name; Quantity = qty; UnitPrice = unitPrice;
        Subtotal = qty * unitPrice;
    }

    public void AddQuantity(int qty) { Quantity += qty; Subtotal = Quantity * UnitPrice; }
}
