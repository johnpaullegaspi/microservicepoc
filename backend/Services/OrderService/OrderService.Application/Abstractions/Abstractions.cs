using BuildingBlocks.Contracts;
using OrderService.Domain.Entities;

namespace OrderService.Application.Abstractions;

public interface IProductRepository
{
    Task<List<Product>> GetAllAsync(CancellationToken ct);
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<Product>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct);
}

public interface IOrderRepository
{
    void Add(Order order);
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Order?> GetByNumberAsync(string orderNumber, CancellationToken ct);
    Task<List<Order>> GetByCustomerAsync(string email, CancellationToken ct);
}

/// <summary>Stages events in the outbox; they are committed atomically with SaveChangesAsync.</summary>
public interface IEventOutbox
{
    void Enqueue<T>(string eventType, T payload, params (BuildingBlocks.Messaging.OutboxDestinationKind Kind, string Destination)[] destinations);
}

public interface IUnitOfWork
{
    /// <summary>Persists changes (business data + outbox rows) in one transaction.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}

public static class OutboxTargets
{
    public static readonly (BuildingBlocks.Messaging.OutboxDestinationKind, string) OrderCreatedQueue =
        (BuildingBlocks.Messaging.OutboxDestinationKind.Queue, QueueNames.OrderCreated);
    public static readonly (BuildingBlocks.Messaging.OutboxDestinationKind, string) OrderEventsTopic =
        (BuildingBlocks.Messaging.OutboxDestinationKind.Topic, TopicNames.OrderEvents);
    public static readonly (BuildingBlocks.Messaging.OutboxDestinationKind, string) NotificationQueue =
        (BuildingBlocks.Messaging.OutboxDestinationKind.Queue, QueueNames.Notification);
}
