using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

public enum OutboxDestinationKind { Queue = 0, Topic = 1 }

/// <summary>Row written in the same DB transaction as the business change (Transactional Outbox).</summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = default!;
    public OutboxDestinationKind DestinationKind { get; set; }
    public string Destination { get; set; } = default!;
    public string Payload { get; set; } = default!;   // serialized EventEnvelope
    public string CorrelationId { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Implemented by each service on top of its own database.</summary>
public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct);
    Task MarkPublishedAsync(Guid id, CancellationToken ct);
    Task MarkFailedAsync(Guid id, string error, CancellationToken ct);
}

public class OutboxDispatcher(IServiceScopeFactory scopes, IMessagePublisher publisher, ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
                foreach (var m in await store.GetPendingAsync(20, ct))
                {
                    try
                    {
                        await publisher.PublishRawAsync(m.DestinationKind, m.Destination, m.Payload, m.EventType, m.CorrelationId, ct);
                        await store.MarkPublishedAsync(m.Id, ct);
                        published++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Outbox publish failed for {Id}; will retry", m.Id);
                        await store.MarkFailedAsync(m.Id, ex.Message, ct);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogWarning("Outbox poll failed (DB not ready?): {Msg}", ex.Message); }

            if (published == 0) await Task.Delay(TimeSpan.FromSeconds(1), ct).ContinueWith(_ => { });
        }
    }
}
