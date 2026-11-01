using BuildingBlocks.Common;

namespace OrderService.Domain.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductName { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string SKU { get; set; } = default!;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string ImageUrl { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = default!; // optimistic concurrency for stock

    public void Reserve(int qty)
    {
        if (qty <= 0) throw new BusinessRuleException("Quantity must be positive.");
        if (StockQuantity < qty)
            throw new BusinessRuleException($"Insufficient stock for '{ProductName}'. Available: {StockQuantity}, requested: {qty}.");
        StockQuantity -= qty;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Release(int qty)
    {
        StockQuantity += qty;
        UpdatedAt = DateTime.UtcNow;
    }
}
