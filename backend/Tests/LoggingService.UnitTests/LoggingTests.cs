using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using LoggingService.Application;
using LoggingService.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LoggingService.UnitTests;

public class LoggingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "logtests-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private FileLogStore Store(long max = 10_000_000, string? dir = null) =>
        new(Options.Create(new FileLogOptions { Directory = dir ?? _dir, MaxFileSizeBytes = max }));

    private static ApplicationLogEvent Log(string msg = "Order successfully created.", string service = "OrderService", string level = "INFO") =>
        new(Guid.NewGuid(), "abc-123", "trace-1", service, "OrderCreated", level, msg, null, "ORD-20261101-001",
            new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc), null);

    [Fact]
    public async Task ReceiveLogEvent_WritesFormattedEntryToTextFile()
    {
        await new LogIngestionService(Store()).IngestAsync(Log(), default);
        var file = Directory.GetFiles(_dir).Single();
        Assert.EndsWith("OrderService-20261101.log", file);
        var text = await File.ReadAllTextAsync(file);
        Assert.Contains("[2026-11-01 10:30:00]", text);
        Assert.Contains("[INFO]", text);
        Assert.Contains("[OrderService]", text);
        Assert.Contains("[CorrelationId: abc-123]", text);
        Assert.Contains("[OrderId: ORD-20261101-001]", text);
        Assert.Contains("Order successfully created.", text);
    }

    [Fact]
    public async Task SensitiveData_IsRedactedBeforeWriting()
    {
        await new LogIngestionService(Store()).IngestAsync(Log("Paid with 4111 1111 1111 1111 cvv=123"), default);
        var text = await File.ReadAllTextAsync(Directory.GetFiles(_dir).Single());
        Assert.DoesNotContain("4111", text);
        Assert.DoesNotContain("cvv=123", text);
    }

    [Fact]
    public async Task MalformedEvent_IsRejectedWithValidationError_AndNothingIsWritten()
    {
        var svc = new LogIngestionService(Store());
        await Assert.ThrowsAsync<ValidationFailedException>(() => svc.IngestAsync(Log(msg: "", level: "LOUD"), default));
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public async Task MalformedQueueMessage_IsDroppedWithoutThrowing()
    {
        var handler = new LogEventHandler(new LogIngestionService(Store()), NullLogger<LogEventHandler>.Instance);
        var bad = BuildingBlocks.Contracts.EventEnvelope<ApplicationLogEvent>.Create("ApplicationLog", "c", Log(msg: ""));
        await handler.HandleAsync(bad, default); // must not throw (would otherwise loop to the DLQ)
    }

    [Fact]
    public async Task FileWriteFailure_Propagates_SoTheQueueCanRetry()
    {
        // a directory path that is actually an existing FILE makes CreateDirectory/Append fail
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "blocked");
        await File.WriteAllTextAsync(blocker, "x");
        var svc = new LogIngestionService(Store(dir: blocker));
        await Assert.ThrowsAnyAsync<Exception>(() => svc.IngestAsync(Log(), default));
    }

    [Fact]
    public async Task SizeBasedRotation_CreatesNumberedFiles()
    {
        var svc = new LogIngestionService(Store(max: 400));
        for (var i = 0; i < 6; i++) await svc.IngestAsync(Log($"entry {i} " + new string('x', 100)), default);
        Assert.True(Directory.GetFiles(_dir).Length >= 2);
    }

    [Fact]
    public async Task DateBasedRotation_UsesOneFilePerDay()
    {
        var svc = new LogIngestionService(Store());
        await svc.IngestAsync(Log() with { Timestamp = new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc) }, default);
        await svc.IngestAsync(Log() with { Timestamp = new DateTime(2026, 11, 2, 1, 0, 0, DateTimeKind.Utc) }, default);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }
}
