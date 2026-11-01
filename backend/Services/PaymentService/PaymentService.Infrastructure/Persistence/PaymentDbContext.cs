using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Persistence;

public class InboxMessage
{
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = default!;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Payment>(e =>
        {
            e.ToTable("Payments", t => t.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.CustomerEmail).HasMaxLength(254).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.PaymentMethod).HasMaxLength(30).IsRequired();
            e.Property(x => x.TransactionReference).HasMaxLength(100);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.OrderNumber);
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

public class PaymentRepository(PaymentDbContext db) : IPaymentRepository
{
    public void Add(Payment p) => db.Payments.Add(p);
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct) => db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken ct) =>
        db.Payments.Where(p => p.OrderId == orderId).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
    public Task<Payment?> GetByIdempotencyKeyAsync(string key, CancellationToken ct) =>
        db.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct);
}

public class EfEventOutbox(PaymentDbContext db) : IEventOutbox
{
    public void Enqueue<T>(string eventType, T payload, params (OutboxDestinationKind Kind, string Destination)[] destinations)
    {
        foreach (var (kind, dest) in destinations)
        {
            var env = EventEnvelope<T>.Create(eventType, CorrelationContext.Current, payload);
            db.OutboxMessages.Add(new OutboxMessage { EventType = eventType, DestinationKind = kind, Destination = dest,
                Payload = MessageSerializer.Serialize(env), CorrelationId = env.CorrelationId });
        }
    }
}

public class EfUnitOfWork(PaymentDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public void ClearTracked() => db.ChangeTracker.Clear();
}

public class EfOutboxStore(PaymentDbContext db) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct) =>
        await db.OutboxMessages.Where(m => m.ProcessedAt == null).OrderBy(m => m.CreatedAt).Take(batchSize).ToListAsync(ct);
    public async Task MarkPublishedAsync(Guid id, CancellationToken ct) =>
        await db.OutboxMessages.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, DateTime.UtcNow).SetProperty(m => m.LastError, (string?)null), ct);
    public async Task MarkFailedAsync(Guid id, string error, CancellationToken ct) =>
        await db.OutboxMessages.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Attempts, m => m.Attempts + 1)
                .SetProperty(m => m.LastError, error.Length > 1000 ? error[..1000] : error), ct);
}

public class EfInboxStore(PaymentDbContext db) : IInboxStore
{
    public Task<bool> HasProcessedAsync(Guid eventId, string consumer, CancellationToken ct) =>
        db.InboxMessages.AnyAsync(i => i.EventId == eventId && i.Consumer == consumer, ct);
    public async Task MarkProcessedAsync(Guid eventId, string consumer, CancellationToken ct)
    {
        db.InboxMessages.Add(new InboxMessage { EventId = eventId, Consumer = consumer });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { }
    }
}
