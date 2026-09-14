using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundgine.Core.Abstractions;

/// <summary>Stable semantic identity for a relationship.</summary>
[JsonConverter(typeof(RelationshipIdJsonConverter))]
public readonly record struct RelationshipId(ulong Value)
{
    public static RelationshipId Create(string semanticEntityName, string semanticRelationshipName) =>
        new(SemanticIdentity.Hash(SemanticIdentity.RelationshipKey(semanticEntityName, semanticRelationshipName)));
}

public sealed class RelationshipIdJsonConverter : JsonConverter<RelationshipId>
{
    public override RelationshipId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new RelationshipId(reader.GetUInt64());

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value))
                return new RelationshipId(value.GetUInt64());
        }

        throw new JsonException("Expected a RelationshipId numeric value or a legacy {\"Value\":...} object.");
    }

    public override void Write(Utf8JsonWriter writer, RelationshipId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);

    public override RelationshipId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        new(ulong.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, RelationshipId value,
        JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}