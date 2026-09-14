using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundgine.Core.Abstractions;

/// <summary>Stable identifier for an AOT authorization predicate.</summary>
[JsonConverter(typeof(AuthorizationIdJsonConverter))]
public readonly record struct AuthorizationId(ulong Value)
{
    public static AuthorizationId Create(string declaringType, string authorizationName) =>
        new(SemanticIdentity.Hash(SemanticIdentity.AuthorizationKey(declaringType, authorizationName)));
}

public sealed class AuthorizationIdJsonConverter : JsonConverter<AuthorizationId>
{
    public override AuthorizationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new AuthorizationId(reader.GetUInt64());

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("Value", out var value))
                return new AuthorizationId(value.GetUInt64());
        }

        throw new JsonException("Expected a AuthorizationId numeric value or a legacy {\"Value\":...} object.");
    }

    public override void Write(Utf8JsonWriter writer, AuthorizationId value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);

    public override AuthorizationId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        new(ulong.Parse(reader.GetString()!));

    public override void WriteAsPropertyName(Utf8JsonWriter writer, AuthorizationId value,
        JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}