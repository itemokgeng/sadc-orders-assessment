using System.Text.Json;

namespace DataLayer.Messaging;

/// <summary>Shared JSON settings (camelCase) so the publisher and the consumer always agree on the message format.</summary>
public static class MessageSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json) => JsonSerializer.Deserialize<T>(utf8Json, Options);
}
