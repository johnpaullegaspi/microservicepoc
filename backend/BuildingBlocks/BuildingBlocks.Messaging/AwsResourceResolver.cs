using System.Collections.Concurrent;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Amazon.SQS.Model;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

/// <summary>Resolves (and optionally provisions) queue URLs and topic ARNs. Retries while LocalStack starts.</summary>
public class AwsResourceResolver(IAmazonSQS sqs, IAmazonSimpleNotificationService sns,
    IOptions<AwsOptions> options, ILogger<AwsResourceResolver> logger)
{
    private readonly ConcurrentDictionary<string, string> _queues = new();
    private readonly ConcurrentDictionary<string, string> _topics = new();
    private readonly AwsOptions _o = options.Value;

    public async Task<string> GetQueueUrlAsync(string name, CancellationToken ct = default)
    {
        if (_queues.TryGetValue(name, out var cached)) return cached;
        string url = null!;
        await Retry.ExecuteAsync(async () =>
        {
            try { url = (await sqs.GetQueueUrlAsync(name, ct)).QueueUrl; }
            catch (QueueDoesNotExistException) when (_o.AutoProvision)
            {
                url = await ProvisionQueueAsync(name, ct);
            }
        }, 12, 1000, (ex, n) => logger.LogWarning("Waiting for queue {Queue} (attempt {N}): {Msg}", name, n, ex.Message), ct);
        _queues[name] = url;
        return url;
    }

    public async Task<string> GetTopicArnAsync(string name, CancellationToken ct = default)
    {
        if (_topics.TryGetValue(name, out var cached)) return cached;
        string arn = null!;
        await Retry.ExecuteAsync(async () => arn = (await sns.CreateTopicAsync(name, ct)).TopicArn, // idempotent
            12, 1000, (ex, n) => logger.LogWarning("Waiting for topic {Topic} (attempt {N}): {Msg}", name, n, ex.Message), ct);
        _topics[name] = arn;
        return arn;
    }

    private async Task<string> ProvisionQueueAsync(string name, CancellationToken ct)
    {
        var attrs = new Dictionary<string, string>
        {
            ["VisibilityTimeout"] = _o.VisibilityTimeoutSeconds.ToString()
        };
        if (!name.EndsWith("-dlq"))
        {
            var dlqUrl = (await sqs.CreateQueueAsync(QueueNames.Dlq(name), ct)).QueueUrl;
            var dlqArn = (await sqs.GetQueueAttributesAsync(dlqUrl, ["QueueArn"], ct)).QueueARN;
            attrs["RedrivePolicy"] = $"{{\"deadLetterTargetArn\":\"{dlqArn}\",\"maxReceiveCount\":\"{_o.MaxReceiveCount}\"}}";
        }
        return (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = name, Attributes = attrs }, ct)).QueueUrl;
    }
}
