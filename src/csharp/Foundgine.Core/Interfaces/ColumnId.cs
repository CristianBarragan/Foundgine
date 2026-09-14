using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundgine.Core.Abstractions;

/// <summary>Stable identity of a physical column.</summary>
[JsonConverter(typeof(ColumnIdJsonConverter))]
public readonly record struct ColumnId(ulong Value)
{
    public static ColumnId Create(string storageName, string columnName) =>
        new(SemanticIdentity.Hash(SemanticIdentity.ColumnKey(storageName, columnName)));
}

public sealed class ColumnIdJsonConverter : JsonConverter<ColumnId>
{
    public override ColumnId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new ColumnId(reader.GetUInt64());

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value))
                return new ColumnId(value.GetUInt64());
        }

        throw new JsonException("Expected a ColumnId numeric value or a legacy {\"Value\":...} object.");
    }

    public override void Write(Utf8JsonWriter writer, ColumnId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);

    public override ColumnId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        new(ulong.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, ColumnId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}