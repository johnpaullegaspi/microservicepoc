using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microservices.IntegrationTests;

public record Ping(string Text);

public class PingHandler : IMessageHandler<Ping>
{
    public static int Handled; public static bool Fail; public static string? SeenCorrelation;
    public Task HandleAsync(EventEnvelope<Ping> m, CancellationToken ct)
    {
        if (Fail) throw new InvalidOperationException("boom");
        Interlocked.Increment(ref Handled);
        SeenCorrelation = CorrelationContext.Current;
        return Task.CompletedTask;
    }
}

public class FakeInbox : IInboxStore
{
    private readonly HashSet<string> _seen = [];
    public Task<bool> HasProcessedAsync(Guid id, string c, CancellationToken ct) => Task.FromResult(_seen.Contains($"{id}|{c}"));
    public Task MarkProcessedAsync(Guid id, string c, CancellationToken ct) { _seen.Add($"{id}|{c}"); return Task.CompletedTask; }
}

/// <summary>Exercises the real SqsConsumer / publisher logic against a substituted SQS client (no AWS or Docker needed).</summary>
public class MessagingTests
{
    private const string Url = "http://sqs/queue";

    private static (SqsConsumer<Ping> consumer, IAmazonSQS sqs) Build(EventEnvelope<Ping> msg, int receiveCount = 1, bool inbox = false)
    {
        PingHandler.Handled = 0; PingHandler.Fail = false; PingHandler.SeenCorrelation = null;
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.GetQueueUrlAsync("q", Arg.Any<CancellationToken>()).Returns(new GetQueueUrlResponse { QueueUrl = Url });
        var delivered = false;
        sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            if (delivered) { await Task.Delay(50); return new ReceiveMessageResponse { Messages = [] }; }
            delivered = true;
            return new ReceiveMessageResponse { Messages = [new Message
            {
                Body = MessageSerializer.Serialize(msg), ReceiptHandle = "rh",
                Attributes = new() { ["ApproximateReceiveCount"] = receiveCount.ToString() }
            }]};
        });

        var services = new ServiceCollection();
        services.AddScoped<IMessageHandler<Ping>, PingHandler>();
        if (inbox) services.AddSingleton<IInboxStore>(new FakeInbox());
        var sp = services.BuildServiceProvider();
        var opts = Options.Create(new AwsOptions { WaitTimeSeconds = 0 });
        var resolver = new AwsResourceResolver(sqs, Substitute.For<IAmazonSimpleNotificationService>(), opts, NullLogger<AwsResourceResolver>.Instance);
        return (new SqsConsumer<Ping>("q", sp.GetRequiredService<IServiceScopeFactory>(), sqs, resolver, opts, NullLogger<SqsConsumer<Ping>>.Instance), sqs);
    }

    private static async Task RunBriefly(SqsConsumer<Ping> c, Func<bool> until)
    {
        await c.StartAsync(default);
        for (var i = 0; i < 100 && !until(); i++) await Task.Delay(50);
        await c.StopAsync(default);
    }

    [Fact]
    public async Task PublishAndConsume_Success_AcknowledgesMessage()
    {
        var (consumer, sqs) = Build(EventEnvelope<Ping>.Create("Ping", "corr", new Ping("hi")));
        await RunBriefly(consumer, () => PingHandler.Handled > 0);
        Assert.Equal(1, PingHandler.Handled);
        await sqs.Received(1).DeleteMessageAsync(Url, "rh", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrelationId_IsPropagatedFromMessageToHandler()
    {
        var (consumer, _) = Build(EventEnvelope<Ping>.Create("Ping", "corr-xyz", new Ping("hi")));
        await RunBriefly(consumer, () => PingHandler.Handled > 0);
        Assert.Equal("corr-xyz", PingHandler.SeenCorrelation);
    }

    [Fact]
    public async Task FailedMessage_IsNotDeleted_AndBacksOffExponentially()
    {
        var (consumer, sqs) = Build(EventEnvelope<Ping>.Create("Ping", "c", new Ping("x")), receiveCount: 3);
        PingHandler.Fail = true;
        await RunBriefly(consumer, () => sqs.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IAmazonSQS.ChangeMessageVisibilityAsync)));
        await sqs.DidNotReceive().DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // receive #3 => 2^3 * 2 = 16 seconds; after MaxReceiveCount receives SQS redrives it to the DLQ
        await sqs.Received().ChangeMessageVisibilityAsync(Url, "rh", 16, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateDelivery_IsNotProcessedTwice()
    {
        var env = EventEnvelope<Ping>.Create("Ping", "c", new Ping("dup"));
        var inbox = new FakeInbox();
        await inbox.MarkProcessedAsync(env.EventId, "q:Ping", default);

        PingHandler.Handled = 0;
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.GetQueueUrlAsync("q", Arg.Any<CancellationToken>()).Returns(new GetQueueUrlResponse { QueueUrl = Url });
        var once = false;
        sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            if (once) { await Task.Delay(50); return new ReceiveMessageResponse { Messages = [] }; }
            once = true;
            return new ReceiveMessageResponse { Messages = [new Message { Body = MessageSerializer.Serialize(env), ReceiptHandle = "rh" }] };
        });
        var services = new ServiceCollection();
        services.AddScoped<IMessageHandler<Ping>, PingHandler>();
        services.AddSingleton<IInboxStore>(inbox);
        var opts = Options.Create(new AwsOptions { WaitTimeSeconds = 0 });
        var consumer = new SqsConsumer<Ping>("q", services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), sqs,
            new AwsResourceResolver(sqs, Substitute.For<IAmazonSimpleNotificationService>(), opts, NullLogger<AwsResourceResolver>.Instance),
            opts, NullLogger<SqsConsumer<Ping>>.Instance);

        await RunBriefly(consumer, () => sqs.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IAmazonSQS.DeleteMessageAsync)));
        Assert.Equal(0, PingHandler.Handled);                       // handler skipped
        await sqs.Received(1).DeleteMessageAsync(Url, "rh", Arg.Any<CancellationToken>()); // but acknowledged
    }

    [Fact]
    public void SnsWrappedMessage_IsUnwrapped()
    {
        var inner = MessageSerializer.Serialize(EventEnvelope<Ping>.Create("Ping", "c", new Ping("sns")));
        var wrapped = System.Text.Json.JsonSerializer.Serialize(new { Type = "Notification", Message = inner });
        Assert.Equal("sns", MessageSerializer.Deserialize<Ping>(wrapped).Payload.Text);
    }

    [Fact]
    public async Task Publisher_SendsEnvelopeWithCorrelationAttribute()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.GetQueueUrlAsync("q", Arg.Any<CancellationToken>()).Returns(new GetQueueUrlResponse { QueueUrl = Url });
        var opts = Options.Create(new AwsOptions());
        var pub = new SqsSnsMessagePublisher(sqs, Substitute.For<IAmazonSimpleNotificationService>(),
            new AwsResourceResolver(sqs, Substitute.For<IAmazonSimpleNotificationService>(), opts, NullLogger<AwsResourceResolver>.Instance));
        await pub.PublishToQueueAsync("q", EventEnvelope<Ping>.Create("Ping", "corr-1", new Ping("p")));
        await sqs.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r =>
            r.QueueUrl == Url && r.MessageAttributes["CorrelationId"].StringValue == "corr-1" && r.MessageBody.Contains("corr-1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void LogSanitizer_RedactsCardsAndSecrets()
    {
        var s = LogSanitizer.Sanitize("card 4111-1111-1111-1111 password: hunter2");
        Assert.DoesNotContain("4111", s);
        Assert.DoesNotContain("hunter2", s);
    }
}
