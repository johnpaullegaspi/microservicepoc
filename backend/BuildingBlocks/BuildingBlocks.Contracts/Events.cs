namespace BuildingBlocks.Contracts;

/// <summary>Envelope shared by every message on SQS/SNS.</summary>
public record EventEnvelope<T>(
    Guid EventId,
    string EventType,
    string CorrelationId,
    DateTime OccurredAt,
    int EventVersion,
    T Payload)
{
    public static EventEnvelope<T> Create(string eventType, string correlationId, T payload, int version = 1) =>
        new(Guid.NewGuid(), eventType, correlationId, DateTime.UtcNow, version, payload);
}

public static class EventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string PaymentSucceeded = "PaymentSucceeded";
    public const string PaymentFailed = "PaymentFailed";
    public const string OrderCancelled = "OrderCancelled";
    public const string RefundProcessed = "RefundProcessed";
    public const string ApplicationLog = "ApplicationLog";
    public const string CustomerNotification = "CustomerNotification";
}

public static class QueueNames
{
    public const string OrderCreated = "order-created-queue";
    public const string PaymentResult = "payment-result-queue";
    public const string Logging = "logging-queue";
    public const string Notification = "notification-queue";
    public static string Dlq(string queue) => queue + "-dlq";
}

public static class TopicNames
{
    public const string OrderEvents = "order-events-topic";
    public const string PaymentEvents = "payment-events-topic";
}

public record OrderItemPayload(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);

public record OrderCreatedEvent(
    Guid OrderId, string OrderNumber, string CustomerName, string CustomerEmail,
    decimal TotalAmount, string PaymentMethod, string IdempotencyKey,
    IReadOnlyList<OrderItemPayload> Items);

public record PaymentSucceededEvent(
    Guid PaymentId, Guid OrderId, string OrderNumber, string CustomerEmail,
    decimal Amount, string TransactionReference);

public record PaymentFailedEvent(
    Guid PaymentId, Guid OrderId, string OrderNumber, string CustomerEmail,
    decimal Amount, string FailureReason);

public record OrderCancelledEvent(Guid OrderId, string OrderNumber, string CustomerEmail, string Reason);

public record RefundProcessedEvent(
    Guid PaymentId, Guid OrderId, string OrderNumber, string CustomerEmail, decimal Amount);

public record ApplicationLogEvent(
    Guid LogId, string CorrelationId, string? TraceId, string ServiceName, string EventType,
    string LogLevel, string Message, string? TransactionId, string? OrderId,
    DateTime Timestamp, string? ExceptionDetails);

public record CustomerNotificationEvent(
    string NotificationType, string CustomerEmail, string CustomerName,
    string OrderNumber, string Subject, string Body);

public static class NotificationTypes
{
    public const string OrderConfirmation = "OrderConfirmation";
    public const string PaymentSuccess = "PaymentSuccess";
    public const string PaymentFailure = "PaymentFailure";
    public const string OrderCancellation = "OrderCancellation";
}
