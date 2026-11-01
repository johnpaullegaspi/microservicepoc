using System.Text.Json;
using BuildingBlocks.Contracts;

namespace BuildingBlocks.Messaging;

public static class MessageSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(EventEnvelope<T> e) => JsonSerializer.Serialize(e, Options);

    /// <summary>Handles raw SQS bodies and SNS-wrapped notifications.</summary>
    public static EventEnvelope<T> Deserialize<T>(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("Type", out var t) && t.GetString() == "Notification" &&
            doc.RootElement.TryGetProperty("Message", out var m))
            body = m.GetString()!;
        return JsonSerializer.Deserialize<EventEnvelope<T>>(body, Options)
               ?? throw new JsonException("Empty message");
    }
}
