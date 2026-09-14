using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundgine.Core.Abstractions;

/// <summary>Stable identity of a semantic model connection.</summary>
[JsonConverter(typeof(ConnectionIdJsonConverter))]
public readonly record struct ConnectionId(ulong Value)
{
    public static ConnectionId Create(string semanticModelName, string semanticConnectionName) =>
        new(SemanticIdentity.Hash(SemanticIdentity.ConnectionKey(semanticModelName, semanticConnectionName)));
}

public sealed class ConnectionIdJsonConverter : JsonConverter<ConnectionId>
{
    public override ConnectionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new ConnectionId(reader.GetUInt64());

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value))
                return new ConnectionId(value.GetUInt64());
        }

        throw new JsonException("Expected a ConnectionId numeric value or a legacy {\"Value\":...} object.");
    }

    public override void Write(Utf8JsonWriter writer, ConnectionId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);

    public override ConnectionId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        new(ulong.Parse(reader.GetString()!));

    public override void
        WriteAsPropertyName(Utf8JsonWriter writer, ConnectionId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}