using BuildingBlocks.Contracts;
using Microsoft.Extensions.Options;

namespace NotificationWorker.Providers;

/// <summary>Extension point: add EmailNotificationProvider / SmsNotificationProvider (SES, SNS SMS) later.</summary>
public interface INotificationProvider
{
    string Name { get; }
    Task SendAsync(CustomerNotificationEvent notification, CancellationToken ct);
}

public class MockNotificationOptions
{
    public const string Section = "Notifications";
    public string? FilePath { get; set; } = "logs/notifications.txt"; // empty => console only
}

/// <summary>Local-dev provider: prints to console and appends to a text file. No real e-mail/SMS is sent.</summary>
public class MockNotificationProvider(IOptions<MockNotificationOptions> options, ILogger<MockNotificationProvider> logger)
    : INotificationProvider
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public string Name => "Mock";

    public async Task SendAsync(CustomerNotificationEvent n, CancellationToken ct)
    {
        var text = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{n.NotificationType}] To: {n.CustomerEmail} | Order: {n.OrderNumber}\n  Subject: {n.Subject}\n  {n.Body}\n";
        logger.LogInformation("NOTIFICATION -> {Email}: {Subject}", n.CustomerEmail, n.Subject);

        var path = options.Value.FilePath;
        if (string.IsNullOrWhiteSpace(path)) return;
        await Gate.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.AppendAllTextAsync(path, text + Environment.NewLine, ct);
        }
        finally { Gate.Release(); }
    }
}
