using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Foundgine.Providers.Storage.Sql.Query;

internal static class CursorCodec
{
    private const int Version = 1;

    public static string Encode(IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("A compound cursor must contain at least one value.", nameof(values));

        var payload = new CursorPayload(Version, values);
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static IReadOnlyList<JsonElement> Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            throw new InvalidOperationException("The pagination cursor is invalid.");

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var payload = JsonSerializer.Deserialize<CursorPayload>(json);

            if (payload is null || payload.Version != Version || payload.Values is null || payload.Values.Count == 0)
                throw new InvalidOperationException("The pagination cursor has an unsupported format.");

            // System.Text.Json deserializes an untyped `object` element as a
            // boxed JsonElement, so each entry needs an explicit unboxing cast.
            return payload.Values.Select(v => (JsonElement)v!).ToArray();
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("The pagination cursor is invalid.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The pagination cursor is invalid.", ex);
        }
    }

    public static object ConvertValue(JsonElement value, Type targetType)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var type = nullableType ?? targetType;

        if (value.ValueKind == JsonValueKind.Null)
        {
            if (nullableType is not null || !targetType.IsValueType)
                return DBNull.Value;

            throw new InvalidOperationException(
                $"A null cursor value cannot be converted to non-nullable '{targetType.Name}'.");
        }

        if (type == typeof(string))
            return value.GetString() ?? string.Empty;
        if (type == typeof(Guid))
            return Guid.Parse(value.GetString()!);
        if (type == typeof(DateTime))
            return value.ValueKind == JsonValueKind.String
                ? DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : value.GetDateTime();
        if (type == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(bool))
            return value.GetBoolean();
        if (type == typeof(byte))
            return value.GetByte();
        if (type == typeof(short))
            return value.GetInt16();
        if (type == typeof(int))
            return value.GetInt32();
        if (type == typeof(long))
            return value.GetInt64();
        if (type == typeof(float))
            return value.GetSingle();
        if (type == typeof(double))
            return value.GetDouble();
        if (type == typeof(decimal))
            return value.GetDecimal();

        return value.Deserialize(type)
               ?? throw new InvalidOperationException(
                   $"Cursor value could not be converted to '{targetType.FullName}'.");
    }

    private sealed record CursorPayload(int Version, IReadOnlyList<object?> Values);
}