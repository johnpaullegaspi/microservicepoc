using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using BuildingBlocks.Contracts;

namespace BuildingBlocks.Messaging;

public interface IMessagePublisher
{
    Task PublishToQueueAsync<T>(string queueName, EventEnvelope<T> message, CancellationToken ct = default);
    Task PublishToTopicAsync<T>(string topicName, EventEnvelope<T> message, CancellationToken ct = default);
    /// <summary>Publishes an already-serialized envelope (used by the outbox dispatcher).</summary>
    Task PublishRawAsync(OutboxDestinationKind kind, string destination, string json, string eventType, string correlationId, CancellationToken ct = default);
}

public class SqsSnsMessagePublisher(IAmazonSQS sqs, IAmazonSimpleNotificationService sns, AwsResourceResolver resolver)
    : IMessagePublisher
{
    public Task PublishToQueueAsync<T>(string queueName, EventEnvelope<T> m, CancellationToken ct = default) =>
        PublishRawAsync(OutboxDestinationKind.Queue, queueName, MessageSerializer.Serialize(m), m.EventType, m.CorrelationId, ct);

    public Task PublishToTopicAsync<T>(string topicName, EventEnvelope<T> m, CancellationToken ct = default) =>
        PublishRawAsync(OutboxDestinationKind.Topic, topicName, MessageSerializer.Serialize(m), m.EventType, m.CorrelationId, ct);

    public async Task PublishRawAsync(OutboxDestinationKind kind, string destination, string json,
        string eventType, string correlationId, CancellationToken ct = default)
    {
        if (kind == OutboxDestinationKind.Queue)
        {
            var url = await resolver.GetQueueUrlAsync(destination, ct);
            await sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = url,
                MessageBody = json,
                MessageAttributes = new()
                {
                    ["EventType"] = new Amazon.SQS.Model.MessageAttributeValue { DataType = "String", StringValue = eventType },
                    ["CorrelationId"] = new Amazon.SQS.Model.MessageAttributeValue { DataType = "String", StringValue = correlationId }
                }
            }, ct);
        }
        else
        {
            var arn = await resolver.GetTopicArnAsync(destination, ct);
            await sns.PublishAsync(new PublishRequest
            {
                TopicArn = arn,
                Message = json,
                MessageAttributes = new()
                {
                    ["EventType"] = new Amazon.SimpleNotificationService.Model.MessageAttributeValue { DataType = "String", StringValue = eventType }
                }
            }, ct);
        }
    }
}
