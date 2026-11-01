using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Domain.Entities;

namespace OrderService.Infrastructure.Persistence;

public class ProductRepository(OrderDbContext db) : IProductRepository
{
    public Task<List<Product>> GetAllAsync(CancellationToken ct) => db.Products.OrderBy(p => p.ProductName).ToListAsync(ct);
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct) => db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
    public Task<List<Product>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return db.Products.Where(p => list.Contains(p.Id)).ToListAsync(ct);
    }
}

public class OrderRepository(OrderDbContext db) : IOrderRepository
{
    public void Add(Order order) => db.Orders.Add(order);
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
    public Task<Order?> GetByNumberAsync(string n, CancellationToken ct) =>
        db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderNumber == n, ct);
    public Task<List<Order>> GetByCustomerAsync(string email, CancellationToken ct) =>
        db.Orders.Include(o => o.Items).Where(o => o.CustomerEmail == email).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
}

public class EfEventOutbox(OrderDbContext db) : IEventOutbox
{
    public void Enqueue<T>(string eventType, T payload, params (OutboxDestinationKind Kind, string Destination)[] destinations)
    {
        foreach (var (kind, dest) in destinations)
        {
            var env = EventEnvelope<T>.Create(eventType, CorrelationContext.Current, payload);
            db.OutboxMessages.Add(new OutboxMessage
            {
                EventType = eventType, DestinationKind = kind, Destination = dest,
                Payload = MessageSerializer.Serialize(env), CorrelationId = env.CorrelationId
            });
        }
    }
}

public class EfUnitOfWork(OrderDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); } // single transaction: entities + outbox rows
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException("Stock changed while placing the order. Please try again.");
        }
    }
}

public class EfOutboxStore(OrderDbContext db) : IOutboxStore
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

public class EfInboxStore(OrderDbContext db) : IInboxStore
{
    public Task<bool> HasProcessedAsync(Guid eventId, string consumer, CancellationToken ct) =>
        db.InboxMessages.AnyAsync(i => i.EventId == eventId && i.Consumer == consumer, ct);

    public async Task MarkProcessedAsync(Guid eventId, string consumer, CancellationToken ct)
    {
        db.InboxMessages.Add(new InboxMessage { EventId = eventId, Consumer = consumer });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* concurrent duplicate already recorded */ }
    }
}
