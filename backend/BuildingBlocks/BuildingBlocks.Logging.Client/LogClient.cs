using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Logging.Client;

public interface IBusinessLogger
{
    void Info(string eventType, string message, string? orderId = null, string? transactionId = null);
    void Warn(string eventType, string message, string? orderId = null, string? transactionId = null);
    void Error(string eventType, string message, Exception? ex = null, string? orderId = null, string? transactionId = null);
}


/// <summary>Strips card numbers / CVVs / secrets before anything leaves the service.</summary>
public static partial class LogSanitizer
{
    [GeneratedRegex(@"\b(?:\d[ -]*?){13,19}\b")] private static partial Regex CardRegex();
    [GeneratedRegex(@"(?i)(cvv|cvc|password|secret|token)\s*[:=]\s*\S+")] private static partial Regex SecretRegex();

    public static string Sanitize(string? s) =>
        string.IsNullOrEmpty(s) ? s ?? "" : SecretRegex().Replace(CardRegex().Replace(s, "[REDACTED-CARD]"), "$1=[REDACTED]");
}

/// <summary>
/// Non-blocking logger: enqueues into a bounded in-memory channel; a background loop publishes to
/// logging-queue. Failures are swallowed so logging can never break a business transaction.
/// </summary>
public class QueueBusinessLogger(IOptionsLike options, IMessagePublisher publisher, ILogger<QueueBusinessLogger> fallback)
    : BackgroundService, IBusinessLogger
{
    private readonly Channel<ApplicationLogEvent> _channel =
        Channel.CreateBounded<ApplicationLogEvent>(new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Info(string t, string m, string? o = null, string? tx = null) => Enqueue("INFO", t, m, null, o, tx);
    public void Warn(string t, string m, string? o = null, string? tx = null) => Enqueue("WARN", t, m, null, o, tx);
    public void Error(string t, string m, Exception? ex = null, string? o = null, string? tx = null) => Enqueue("ERROR", t, m, ex, o, tx);

    private void Enqueue(string level, string type, string message, Exception? ex, string? orderId, string? txId)
    {
        try
        {
            _channel.Writer.TryWrite(new ApplicationLogEvent(Guid.NewGuid(), CorrelationContext.Current,
                Activity.Current?.TraceId.ToString(), options.ServiceName, type, level,
                LogSanitizer.Sanitize(message), txId, orderId, DateTime.UtcNow,
                ex is null ? null : LogSanitizer.Sanitize(ex.ToString())));
        }
        catch { /* never throw from logging */ }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var log in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var env = EventEnvelope<ApplicationLogEvent>.Create(EventTypes.ApplicationLog, log.CorrelationId, log);
                    await publisher.PublishToQueueAsync(QueueNames.Logging, env, ct);
                }
                catch (Exception e)
                {
                    fallback.LogWarning("Could not publish log event ({Type}): {Msg}", log.EventType, e.Message);
                    await Task.Delay(500, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}

/// <summary>Tiny abstraction so the logger doesn't depend on IOptions plumbing.</summary>
public interface IOptionsLike { string ServiceName { get; } }
public class ServiceNameProvider(string name) : IOptionsLike { public string ServiceName { get; } = name; }

public static class LoggingClientRegistration
{
    public static IServiceCollection AddBusinessLogging(this IServiceCollection services, string serviceName)
    {
        services.AddSingleton<IOptionsLike>(new ServiceNameProvider(serviceName));
        services.AddSingleton<QueueBusinessLogger>();
        services.AddSingleton<IBusinessLogger>(sp => sp.GetRequiredService<QueueBusinessLogger>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<QueueBusinessLogger>());
        return services;
    }
}
