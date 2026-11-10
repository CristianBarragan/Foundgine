using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Foundgine.Providers.Orm;

/// <summary>
/// Opaque forward cursor: the ordering tuple of the last row of a page, as invariant strings in a JSON array,
/// base64url encoded. Values are re-typed on decode by the generated field bindings (OrmValue.Coerce), so no
/// serializer or reflection over entity types is involved. A cursor only positions a query that is still
/// authorized and filtered by the server, so a forged cursor cannot widen access. Not interchangeable with
/// cursors from the SQL provider.
/// </summary>
public static class OrmCursor
{
    public static string Encode(IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var value in values)
            {
                if (value is null) writer.WriteNullValue();
                else writer.WriteStringValue(Format(value));
            }

            writer.WriteEndArray();
        }

        return Base64Url.EncodeToString(stream.ToArray());
    }

    /// <summary>
    /// Decodes an integer aggregate component from a cursor tuple. COUNT is non-nullable and
    /// uses invariant decimal text so its representation is independent of the host culture.
    /// This is deliberately a typed helper: callers must not treat arbitrary cursor strings as
    /// aggregate values or silently coerce malformed values.
    /// </summary>
    public static int DecodeInt32(IReadOnlyList<string?> values, int index)
    {
        ArgumentNullException.ThrowIfNull(values);
        if ((uint)index >= (uint)values.Count)
            throw new InvalidOperationException("The pagination cursor does not contain the requested aggregate component.");
        var value = values[index];
        if (value is null)
            throw new NotSupportedException("ORM provider: a COUNT aggregate cursor component cannot be null.");
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            !string.Equals(value, parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new InvalidOperationException("The pagination cursor contains an invalid COUNT aggregate value.");
        return parsed;
    }

    /// <summary>Formats a non-null COUNT value using the same invariant representation as cursor tuples.</summary>
    public static string EncodeInt32(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "A COUNT aggregate cursor value cannot be negative.");
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Encodes a mixed ordering tuple while enforcing the declared Int32 slots (used for COUNT).
    /// Slot indexes refer to the complete ordering tuple, not to a filtered list of aggregates.
    /// </summary>
    public static string EncodeWithInt32Components(
        IReadOnlyList<object?> values,
        IReadOnlyCollection<int> int32ComponentIndexes)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(int32ComponentIndexes);
        var indexes = ValidateComponentIndexes(values.Count, int32ComponentIndexes);
        var normalized = new object?[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (indexes.Contains(i))
            {
                if (value is not int count)
                    throw new InvalidOperationException("An ORM COUNT cursor component must be materialized as Int32.");
                normalized[i] = EncodeInt32(count);
            }
            else
            {
                normalized[i] = value;
            }
        }
        return Encode(normalized);
    }

    /// <summary>Decodes a mixed tuple and strictly validates each declared Int32 (COUNT) component.</summary>
    public static IReadOnlyList<string?> DecodeWithInt32Components(
        string cursor,
        IReadOnlyCollection<int> int32ComponentIndexes)
    {
        ArgumentNullException.ThrowIfNull(int32ComponentIndexes);
        var values = Decode(cursor);
        var indexes = ValidateComponentIndexes(values.Count, int32ComponentIndexes);
        foreach (var index in indexes)
            _ = DecodeInt32(values, index);
        return values;
    }

    private static HashSet<int> ValidateComponentIndexes(int valueCount, IReadOnlyCollection<int> indexes)
    {
        var result = new HashSet<int>();
        foreach (var index in indexes)
        {
            if ((uint)index >= (uint)valueCount)
                throw new ArgumentOutOfRangeException(nameof(indexes), "A typed cursor component index is outside the tuple.");
            if (!result.Add(index))
                throw new ArgumentException("Typed cursor component indexes must be unique.", nameof(indexes));
        }
        return result;
    }

    public static IReadOnlyList<string?> Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            throw new InvalidOperationException("The pagination cursor is empty.");

        try
        {
            var bytes = Base64Url.DecodeFromChars(cursor);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new FormatException("Cursor payload is not an array.");

            var result = new List<string?>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                result.Add(item.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => item.GetString(),
                    _ => throw new FormatException("Cursor payload contains a non-string value.")
                });
            }

            return result;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new InvalidOperationException("The pagination cursor is malformed.", ex);
        }
    }

    private static string Format(object value) => value switch
    {
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("O", CultureInfo.InvariantCulture),
        Enum e => Convert.ToString(Convert.ChangeType(e, e.GetTypeCode(), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)!,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
