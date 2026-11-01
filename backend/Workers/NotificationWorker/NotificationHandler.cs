using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using NotificationWorker.Providers;

namespace NotificationWorker;

/// <summary>Handles order confirmation, payment success/failure and cancellation notifications.</summary>
public class NotificationHandler(INotificationProvider provider, IBusinessLogger log) : IMessageHandler<CustomerNotificationEvent>
{
    private static readonly HashSet<string> Known =
    [
        NotificationTypes.OrderConfirmation, NotificationTypes.PaymentSuccess,
        NotificationTypes.PaymentFailure, NotificationTypes.OrderCancellation
    ];

    public async Task HandleAsync(EventEnvelope<CustomerNotificationEvent> m, CancellationToken ct)
    {
        var n = m.Payload;
        if (!Known.Contains(n.NotificationType))
        {
            log.Warn("NotificationSkipped", $"Unknown notification type '{n.NotificationType}'", n.OrderNumber);
            return;
        }
        await provider.SendAsync(n, ct);
        log.Info("NotificationDelivered", $"{n.NotificationType} delivered via {provider.Name} to customer", n.OrderNumber);
    }
}
