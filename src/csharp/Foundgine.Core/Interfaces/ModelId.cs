using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundgine.Core.Abstractions;

/// <summary>Stable identity of a semantic model.</summary>
[JsonConverter(typeof(ModelIdJsonConverter))]
public readonly record struct ModelId(ulong Value)
{
    public static ModelId Create(string semanticName) =>
        new(SemanticIdentity.Hash(SemanticIdentity.ModelKey(semanticName)));
}

public sealed class ModelIdJsonConverter : JsonConverter<ModelId>
{
    public override ModelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new ModelId(reader.GetUInt64());

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value))
                return new ModelId(value.GetUInt64());
        }

        throw new JsonException("Expected a ModelId numeric value or a legacy {\"Value\":...} object.");
    }

    public override void Write(Utf8JsonWriter writer, ModelId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);

    public override ModelId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        new(ulong.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, ModelId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}