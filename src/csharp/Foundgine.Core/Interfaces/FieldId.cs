using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundgine.Core.Abstractions;

[JsonConverter(typeof(FieldIdJsonConverter))]
public readonly record struct FieldId(ulong Value)
{
    public static FieldId Create(string semanticEntityName, string semanticFieldName) =>
        new(SemanticIdentity.Hash(SemanticIdentity.FieldKey(semanticEntityName, semanticFieldName)));
}

public sealed class FieldIdJsonConverter : JsonConverter<FieldId>
{
    public override FieldId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new FieldId(reader.GetUInt64());
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value))
                return new FieldId(value.GetUInt64());
        }

        throw new JsonException("Expected a FieldId numeric value or a legacy {\"Value\":...} object.");
    }

    public override void Write(Utf8JsonWriter writer, FieldId value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Value);

    public override FieldId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
        => new(ulong.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, FieldId value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value.ToString());
}