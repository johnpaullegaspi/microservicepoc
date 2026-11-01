using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;

namespace OrderService.Infrastructure.Persistence;

public class InboxMessage
{
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = default!;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>(e =>
        {
            e.ToTable("Products", t => t.HasCheckConstraint("CK_Products_Stock", "[StockQuantity] >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            e.Property(x => x.SKU).HasMaxLength(50).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.ImageUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.SKU).IsUnique();
            e.HasData(Seed.Products);
        });

        b.Entity<Order>(e =>
        {
            e.ToTable("Orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
            e.Property(x => x.CustomerEmail).HasMaxLength(254).IsRequired();
            e.Property(x => x.ContactNumber).HasMaxLength(30).IsRequired();
            e.Property(x => x.DeliveryAddress).HasMaxLength(500).IsRequired();
            e.Property(x => x.PaymentMethod).HasMaxLength(30).IsRequired();
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.OrderStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.TransactionReference).HasMaxLength(100);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasIndex(x => x.CustomerEmail);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<OrderItem>(e =>
        {
            e.ToTable("OrderItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OrderId);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(100);
            e.Property(x => x.Destination).HasMaxLength(200);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasIndex(x => new { x.ProcessedAt, x.CreatedAt });
        });

        b.Entity<InboxMessage>(e =>
        {
            e.ToTable("InboxMessages");
            e.HasKey(x => new { x.EventId, x.Consumer });
            e.Property(x => x.Consumer).HasMaxLength(200);
        });
    }
}

public static class Seed
{
    private static Product P(string id, string name, string desc, string sku, decimal price, int stock, string img) => new()
    {
        Id = Guid.Parse(id), ProductName = name, Description = desc, SKU = sku, Price = price, StockQuantity = stock,
        ImageUrl = img, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    public static readonly Product[] Products =
    [
        P("11111111-1111-1111-1111-111111111111", "Nike Air Max", "Iconic visible Air cushioning with all-day comfort.", "NIKE-AM-001", 159.99m, 25, "/images/air-max.svg"),
        P("22222222-2222-2222-2222-222222222222", "Nike Air Force 1", "The classic court silhouette, reborn for the street.", "NIKE-AF1-002", 119.99m, 40, "/images/air-force-1.svg"),
        P("33333333-3333-3333-3333-333333333333", "Nike Dunk Low", "Retro basketball style with bold colour blocking.", "NIKE-DL-003", 109.99m, 30, "/images/dunk-low.svg"),
        P("44444444-4444-4444-4444-444444444444", "Nike Pegasus", "Responsive running shoe built for daily miles.", "NIKE-PG-004", 139.99m, 35, "/images/pegasus.svg")
    ];
}
