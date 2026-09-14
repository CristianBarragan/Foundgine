using System.Text.Json;
using System.Text.Json.Serialization;
using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Metadata;

/// <summary>Identity of a physical storage entity such as a database table.</summary>
[JsonConverter(typeof(StorageEntityIdJsonConverter))]
public readonly record struct StorageEntityId(ulong Value)
{
    public static StorageEntityId Create(string storageName) =>
        new(SemanticIdentity.Hash(SemanticIdentity.TableKey(storageName)));
}

public sealed class StorageEntityIdJsonConverter : JsonConverter<StorageEntityId>
{
    public override StorageEntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new StorageEntityId(reader.GetUInt64());
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value))
                return new StorageEntityId(value.GetUInt64());
        }

        throw new JsonException("Expected a StorageEntityId numeric value or a legacy {\"Value\":...} object.");
    }

    public override void Write(Utf8JsonWriter writer, StorageEntityId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);

    public override StorageEntityId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => new(ulong.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, StorageEntityId value,
        JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}