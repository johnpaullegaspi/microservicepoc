using OrderService.Domain.Entities;

namespace OrderService.Application.Dtos;

public record ProductDto(Guid Id, string ProductName, string Description, string SKU, decimal Price, int StockQuantity, string ImageUrl);
public record OrderItemDto(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal Subtotal);
public record OrderDto(Guid Id, string OrderNumber, string CustomerName, string CustomerEmail, string ContactNumber,
    string DeliveryAddress, string PaymentMethod, decimal TotalAmount, string OrderStatus, string PaymentStatus,
    string? TransactionReference, string? FailureReason, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<OrderItemDto> Items);
public record OrderItemRequest(Guid ProductId, int Quantity);

public static class Mappings
{
    public static ProductDto ToDto(this Product p) => new(p.Id, p.ProductName, p.Description, p.SKU, p.Price, p.StockQuantity, p.ImageUrl);
    public static OrderDto ToDto(this Order o) => new(o.Id, o.OrderNumber, o.CustomerName, o.CustomerEmail, o.ContactNumber,
        o.DeliveryAddress, o.PaymentMethod, o.TotalAmount, o.OrderStatus.ToString(), o.PaymentStatus.ToString(),
        o.TransactionReference, o.FailureReason, o.CreatedAt, o.UpdatedAt,
        o.Items.Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.Subtotal)).ToList());
}
