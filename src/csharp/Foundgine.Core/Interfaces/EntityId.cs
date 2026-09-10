using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundgine.Core.Abstractions;

/// <summary>
/// Stable identity of a semantic entity. The value is derived from the
/// canonical semantic entity name and is independent of declaration order,
/// CLR metadata tokens, or registration order.
/// </summary>
[JsonConverter(typeof(EntityIdJsonConverter))]
public readonly record struct EntityId(ulong Value)
{
    public static EntityId Create(string semanticName) =>
        new(SemanticIdentity.Hash(SemanticIdentity.EntityKey(semanticName)));
}

public sealed class EntityIdJsonConverter : JsonConverter<EntityId>
{
    public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new EntityId(reader.GetUInt64());

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value))
                return new EntityId(value.GetUInt64());
        }

        throw new JsonException("Expected a EntityId numeric value or a legacy {\"Value\":...} object.");
    }

    public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);

    public override EntityId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        new(ulong.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}