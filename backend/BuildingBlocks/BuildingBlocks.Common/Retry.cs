namespace BuildingBlocks.Common;

public static class Retry
{
    /// <summary>Retries with exponential backoff; used for DB init and AWS startup races.</summary>
    public static async Task ExecuteAsync(Func<Task> action, int maxAttempts = 10, int baseDelayMs = 1000,
        Action<Exception, int>? onRetry = null, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await action(); return; }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                onRetry?.Invoke(ex, attempt);
                await Task.Delay(Math.Min(baseDelayMs * (int)Math.Pow(2, attempt - 1), 15000), ct);
            }
        }
    }
}
