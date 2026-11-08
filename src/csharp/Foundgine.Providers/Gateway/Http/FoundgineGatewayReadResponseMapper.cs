using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Foundgine.Core.Execution;

namespace Foundgine.Providers.Gateway.Http;

internal static class FoundgineGatewayReadResponseMapper
{
    public static FoundgineGatewayReadResponse Map(ExecutionResult result)
    {
        var rows = new FoundgineGatewayReadRow[result.Rows.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var pair in result.Rows[i].Values)
                values.Add(pair.Key, ToJsonElement(pair.Value));
            rows[i] = new FoundgineGatewayReadRow(values);
        }
        var page = result.PageInfo is null ? null : new FoundgineGatewayReadPageInfo(
            result.PageInfo.StartCursor, result.PageInfo.EndCursor,
            result.PageInfo.HasNextPage, result.PageInfo.HasPreviousPage);
        return new FoundgineGatewayReadResponse(rows, page);
    }

    private static JsonElement ToJsonElement(object? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteValue(writer, value, 0);
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, int depth)
    {
        if (depth > 16) throw new NotSupportedException("Execution result value nesting exceeds the gateway limit.");
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case byte n: writer.WriteNumberValue(n); break;
            case sbyte n: writer.WriteNumberValue(n); break;
            case short n: writer.WriteNumberValue(n); break;
            case ushort n: writer.WriteNumberValue(n); break;
            case int n: writer.WriteNumberValue(n); break;
            case uint n: writer.WriteNumberValue(n); break;
            case long n: writer.WriteNumberValue(n); break;
            case ulong n: writer.WriteNumberValue(n); break;
            case float n when float.IsFinite(n): writer.WriteNumberValue(n); break;
            case double n when double.IsFinite(n): writer.WriteNumberValue(n); break;
            case decimal n: writer.WriteNumberValue(n); break;
            case Guid id: writer.WriteStringValue(id); break;
            case DateTime date: writer.WriteStringValue(date); break;
            case DateTimeOffset date: writer.WriteStringValue(date); break;
            case DateOnly date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeOnly time: writer.WriteStringValue(time.ToString("O", CultureInfo.InvariantCulture)); break;
            case byte[] bytes: writer.WriteBase64StringValue(bytes); break;
            case JsonElement element: element.WriteTo(writer); break;
            case Enum e: writer.WriteStringValue(e.ToString()); break;
            case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                writer.WriteStartObject();
                foreach (var pair in readOnlyDictionary) { writer.WritePropertyName(pair.Key); WriteValue(writer, pair.Value, depth + 1); }
                writer.WriteEndObject(); break;
            case IDictionary<string, object?> genericDictionary:
                writer.WriteStartObject();
                foreach (var pair in genericDictionary) { writer.WritePropertyName(pair.Key); WriteValue(writer, pair.Value, depth + 1); }
                writer.WriteEndObject(); break;
            case IDictionary dictionary:
                writer.WriteStartObject();
                foreach (DictionaryEntry pair in dictionary)
                {
                    if (pair.Key is not string key) throw new NotSupportedException("Gateway result dictionaries require string keys.");
                    writer.WritePropertyName(key); WriteValue(writer, pair.Value, depth + 1);
                }
                writer.WriteEndObject(); break;
            case IEnumerable items:
                writer.WriteStartArray(); foreach (var item in items) WriteValue(writer, item, depth + 1); writer.WriteEndArray(); break;
            default: throw new NotSupportedException($"Execution result type '{value.GetType().FullName}' has no gateway AOT wire mapping.");
        }
    }
}
