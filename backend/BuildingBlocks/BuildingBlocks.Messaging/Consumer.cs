using Amazon.SQS;
using Amazon.SQS.Model;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

public interface IMessageHandler<T>
{
    /// <summary>Must be safe to run more than once for the same EventId (at-least-once delivery).</summary>
    Task HandleAsync(EventEnvelope<T> message, CancellationToken ct);
}

/// <summary>Inbox / duplicate protection. Implemented per service on its own database.</summary>
public interface IInboxStore
{
    Task<bool> HasProcessedAsync(Guid eventId, string consumer, CancellationToken ct);
    Task MarkProcessedAsync(Guid eventId, string consumer, CancellationToken ct);
}

/// <summary>
/// Generic SQS long-polling consumer: acknowledges (deletes) only after successful handling, applies
/// exponential backoff via visibility timeout on failure, and relies on the queue's redrive policy to move
/// poison messages to the DLQ after MaxReceiveCount attempts.
/// </summary>
public class SqsConsumer<T>(string queueName, IServiceScopeFactory scopes, IAmazonSQS sqs,
    AwsResourceResolver resolver, IOptions<AwsOptions> options, ILogger<SqsConsumer<T>> logger) : BackgroundService
{
    private readonly AwsOptions _o = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string url;
        try { url = await resolver.GetQueueUrlAsync(queueName, ct); }
        catch (OperationCanceledException) { return; }
        logger.LogInformation("Consumer for {Queue} started", queueName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var resp = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = url,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = _o.WaitTimeSeconds,
                    VisibilityTimeout = _o.VisibilityTimeoutSeconds,
                    MessageSystemAttributeNames = ["ApproximateReceiveCount"]
                }, ct);

                foreach (var msg in resp.Messages ?? [])
                    await ProcessAsync(url, msg, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Receive from {Queue} failed: {Msg}", queueName, ex.Message);
                await Task.Delay(3000, ct).ContinueWith(_ => { });
            }
        }
    }

    private async Task ProcessAsync(string url, Message msg, CancellationToken ct)
    {
        EventEnvelope<T>? envelope = null;
        try
        {
            envelope = MessageSerializer.Deserialize<T>(msg.Body);
            CorrelationContext.Current = envelope.CorrelationId;

            using var scope = scopes.CreateScope();
            var inbox = scope.ServiceProvider.GetService<IInboxStore>();
            var consumerName = $"{queueName}:{typeof(T).Name}";

            if (inbox is not null && await inbox.HasProcessedAsync(envelope.EventId, consumerName, ct))
            {
                logger.LogInformation("Duplicate {EventId} on {Queue} skipped", envelope.EventId, queueName);
            }
            else
            {
                var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler<T>>();
                await handler.HandleAsync(envelope, ct);
                if (inbox is not null) await inbox.MarkProcessedAsync(envelope.EventId, consumerName, ct);
            }
            await sqs.DeleteMessageAsync(url, msg.ReceiptHandle, ct); // acknowledge
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var receives = int.TryParse(msg.Attributes?.GetValueOrDefault("ApproximateReceiveCount"), out var n) ? n : 1;
            var delay = Math.Min((int)Math.Pow(2, receives) * 2, _o.MaxBackoffSeconds); // exponential backoff
            logger.LogError(ex, "Handling {Queue} message {Id} failed (receive #{N}); retry in {Delay}s",
                queueName, envelope?.EventId, receives, delay);
            try { await sqs.ChangeMessageVisibilityAsync(url, msg.ReceiptHandle, delay, ct); } catch { /* best effort */ }
            // After MaxReceiveCount receives SQS moves the message to <queue>-dlq (redrive policy).
        }
    }
}

public static class ConsumerRegistration
{
    public static IServiceCollection AddSqsConsumer<T, THandler>(this IServiceCollection services, string queueName)
        where THandler : class, IMessageHandler<T>
    {
        services.AddScoped<IMessageHandler<T>, THandler>();
        services.AddSingleton<IHostedService>(sp => new SqsConsumer<T>(queueName,
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IAmazonSQS>(),
            sp.GetRequiredService<AwsResourceResolver>(), sp.GetRequiredService<IOptions<AwsOptions>>(),
            sp.GetRequiredService<ILogger<SqsConsumer<T>>>()));
        return services;
    }

    public static IServiceCollection AddOutboxDispatcher<TStore>(this IServiceCollection services)
        where TStore : class, IOutboxStore
    {
        services.AddScoped<IOutboxStore, TStore>();
        services.AddHostedService<OutboxDispatcher>();
        return services;
    }
}
