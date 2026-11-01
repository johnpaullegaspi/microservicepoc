using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoggingService.Application;

public interface ILogStore
{
    Task AppendAsync(ApplicationLogEvent log, CancellationToken ct);
}

public record LogRequest(string? CorrelationId, string? TraceId, string ServiceName, string EventType, string LogLevel,
    string Message, string? TransactionId, string? OrderId, DateTime? Timestamp, string? ExceptionDetails);

public static class LogFormatter
{
    /// <summary>
    /// [2026-11-01 10:30:00]
    /// [INFO]
    /// [OrderService]
    /// [CorrelationId: abc-123]
    /// [OrderId: ORD-...]
    /// Message...
    /// </summary>
    public static string Format(ApplicationLogEvent l)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{l.Timestamp.ToUniversalTime():yyyy-MM-dd HH:mm:ss}]");
        sb.AppendLine($"[{l.LogLevel.ToUpperInvariant()}]");
        sb.AppendLine($"[{l.ServiceName}]");
        sb.AppendLine($"[CorrelationId: {l.CorrelationId}]");
        sb.AppendLine($"[OrderId: {l.OrderId ?? "-"}]");
        sb.AppendLine($"[LogId: {l.LogId}] [EventType: {l.EventType}] [TraceId: {l.TraceId ?? "-"}] [TransactionId: {l.TransactionId ?? "-"}]");
        sb.AppendLine(l.Message);
        if (!string.IsNullOrWhiteSpace(l.ExceptionDetails))
        {
            sb.AppendLine("[Exception]");
            sb.AppendLine(l.ExceptionDetails);
        }
        sb.AppendLine();
        return sb.ToString();
    }
}

public class LogIngestionService(ILogStore store)
{
    private static readonly string[] Levels = ["TRACE", "DEBUG", "INFO", "WARN", "WARNING", "ERROR", "CRITICAL"];

    /// <summary>Validates, sanitizes (defence in depth) and persists. Throws ValidationFailedException on malformed input.</summary>
    public async Task IngestAsync(ApplicationLogEvent log, CancellationToken ct)
    {
        Validate(log);
        var clean = log with
        {
            Message = LogSanitizer.Sanitize(log.Message),
            ExceptionDetails = log.ExceptionDetails is null ? null : LogSanitizer.Sanitize(log.ExceptionDetails),
            LogLevel = log.LogLevel.ToUpperInvariant()
        };
        await store.AppendAsync(clean, ct);
    }

    public Task IngestAsync(LogRequest r, CancellationToken ct) => IngestAsync(new ApplicationLogEvent(
        Guid.NewGuid(), r.CorrelationId ?? CorrelationContext.Current, r.TraceId, r.ServiceName ?? "", r.EventType ?? "",
        r.LogLevel ?? "", r.Message ?? "", r.TransactionId, r.OrderId, r.Timestamp ?? DateTime.UtcNow, r.ExceptionDetails), ct);

    private static void Validate(ApplicationLogEvent l)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(l.ServiceName)) errors["serviceName"] = ["ServiceName is required."];
        if (string.IsNullOrWhiteSpace(l.Message)) errors["message"] = ["Message is required."];
        if (string.IsNullOrWhiteSpace(l.CorrelationId)) errors["correlationId"] = ["CorrelationId is required."];
        if (!Levels.Contains(l.LogLevel?.ToUpperInvariant() ?? "")) errors["logLevel"] = ["LogLevel must be one of " + string.Join(", ", Levels)];
        if (errors.Count > 0) throw new ValidationFailedException(errors);
    }
}

/// <summary>Consumes logging-queue. Malformed events never block the queue: invalid ones are acknowledged and counted.</summary>
public class LogEventHandler(LogIngestionService ingestion, ILogger<LogEventHandler> logger)
    : IMessageHandler<ApplicationLogEvent>
{
    public async Task HandleAsync(EventEnvelope<ApplicationLogEvent> m, CancellationToken ct)
    {
        try { await ingestion.IngestAsync(m.Payload, ct); }
        catch (ValidationFailedException ex)
        {
            // Poison message: retrying cannot fix it, so drop it (acknowledge) after recording why.
            logger.LogWarning("Dropping malformed log event {EventId}: {Errors}", m.EventId,
                string.Join("; ", ex.Errors.SelectMany(e => e.Value)));
        }
        // IO failures propagate -> SQS retry with backoff -> DLQ (no log loss)
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddLoggingApplication(this IServiceCollection services)
    {
        services.AddScoped<LogIngestionService>();
        return services;
    }
}
