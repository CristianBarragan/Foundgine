using System.Text.Json;
using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Regression coverage for a bug where mutation execution results
/// (IReadOnlyDictionary&lt;FieldId, object?&gt; ReturnedValues, see
/// Foundgine.Core.Execution.Mutation.MutationResult) threw
///
///     System.NotSupportedException: The type 'Foundgine.Core.Abstractions.FieldId'
///     is not a supported dictionary key using converter of type
///     'ObjectDefaultConverter`1[FieldId]' ...
///
/// the moment they were serialized (e.g. by an MCP tool response or a
/// GraphQL AnyType field). System.Text.Json's default converter for a
/// struct cannot double as a dictionary-key converter unless it explicitly
/// overrides ReadAsPropertyName/WriteAsPropertyName - see
/// FieldIdJsonConverter in FieldId.cs.
/// </summary>
public sealed class FieldIdJsonSerializationTests
{
    [Fact]
    public void FieldId_serializes_as_a_plain_numeric_value()
    {
        var json = JsonSerializer.Serialize(new FieldId(7));

        Assert.Equal("7", json);
    }

    [Fact]
    public void FieldId_round_trips_as_a_plain_value()
    {
        var json = JsonSerializer.Serialize(new FieldId(42));

        var result = JsonSerializer.Deserialize<FieldId>(json);

        Assert.Equal(new FieldId(42), result);
    }

    [Fact]
    public void Dictionary_keyed_by_FieldId_serializes_without_throwing()
    {
        // This is the exact shape that crashed: a dictionary keyed by the
        // struct, nested inside an object with other properties - matching
        // MutationResult.ReturnedValues as returned to a GraphQL/MCP caller.
        var values = new Dictionary<FieldId, object?>
        {
            [new FieldId(1)] = "TRK-1'; DROP TABLE shipments; --",
            [new FieldId(2)] = 42,
        };
        var wrapped = new { shipment = new { ReturnedValues = values } };

        var exception = Record.Exception(() => JsonSerializer.Serialize(wrapped));

        Assert.Null(exception);
    }

    [Fact]
    public void Dictionary_keyed_by_FieldId_round_trips_through_serialization()
    {
        var values = new Dictionary<FieldId, object?>
        {
            [new FieldId(3)] = "some-value",
        };

        var json = JsonSerializer.Serialize(values);
        var result = JsonSerializer.Deserialize<Dictionary<FieldId, object?>>(json);

        Assert.NotNull(result);
        Assert.True(result!.ContainsKey(new FieldId(3)));
    }

    [Fact]
    public void Dictionary_keyed_by_FieldId_preserves_arbitrary_string_values_including_quotes()
    {
        // Guards specifically against the injection-payload class of value
        // (embedded single quotes) getting mangled or dropped rather than
        // merely escaped on the wire.
        const string payload = "TRK-1'; DROP TABLE shipments; --";
        var values = new Dictionary<FieldId, object?> { [new FieldId(9)] = payload };

        var json = JsonSerializer.Serialize(values);
        var result = JsonSerializer.Deserialize<Dictionary<FieldId, object?>>(json);

        Assert.Equal(payload, result![new FieldId(9)]!.ToString());
    }
}
