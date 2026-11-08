using System.Text.Json;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Providers.Gateway.Http;

/// <summary>Wire DTO for a bounded, provider-neutral read intent.</summary>
public sealed class FoundgineGatewayReadRequest
{
    public string? RootEntity { get; set; }
    public List<FoundgineGatewayReadSelection>? Selections { get; set; }
    public FoundgineGatewayReadFilter? Filter { get; set; }
    public List<FoundgineGatewayReadOrder>? Order { get; set; }
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public string? After { get; set; }
}

public sealed class FoundgineGatewayReadSelection
{
    public string? Field { get; set; }
    public string? Relationship { get; set; }
    public List<FoundgineGatewayReadSelection>? Children { get; set; }
}

public sealed class FoundgineGatewayReadFilter
{
    public string? Kind { get; set; }
    public string? Field { get; set; }
    public string? Operator { get; set; }
    public JsonElement Value { get; set; }
    public string? Relationship { get; set; }
    public string? Quantifier { get; set; }
    public FoundgineGatewayReadFilter? Predicate { get; set; }
    public List<FoundgineGatewayReadFilter>? Expressions { get; set; }
}

public sealed class FoundgineGatewayReadOrder
{
    public string? Field { get; set; }
    public string? Direction { get; set; }
    public List<string>? RelationshipPath { get; set; }
    public string? Aggregate { get; set; }
}

/// <summary>Public, deliberately minimized read response; runtime evidence is not implicitly serialized.</summary>
public sealed record FoundgineGatewayReadResponse(
    IReadOnlyList<FoundgineGatewayReadRow> Rows,
    FoundgineGatewayReadPageInfo? PageInfo);

public sealed record FoundgineGatewayReadRow(IReadOnlyDictionary<string, JsonElement> Values);
public sealed record FoundgineGatewayReadPageInfo(
    string? StartCursor, string? EndCursor, bool HasNextPage, bool HasPreviousPage);

internal static class FoundgineGatewayReadRequestMapper
{
    public static ReadIntent ToIntent(FoundgineGatewayReadRequest request, FoundgineGatewayHttpOptions limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateLimits(limits);
        if (string.IsNullOrWhiteSpace(request.RootEntity)) throw new ArgumentException("rootEntity is required.");
        if (request.Selections is not { Count: > 0 }) throw new ArgumentException("At least one selection is required.");
        var selectionCount = 0;
        var selections = new ReadSelection[request.Selections.Count];
        for (var i = 0; i < selections.Length; i++)
            selections[i] = MapSelection(request.Selections[i], 1, ref selectionCount, limits);
        if (request.Limit is < 0 || request.Offset is < 0) throw new ArgumentException("limit and offset cannot be negative.");
        var effectiveLimit = request.Limit ?? limits.DefaultReadLimit;
        if (effectiveLimit < 1 || effectiveLimit > limits.MaxReadLimit) throw new ArgumentException($"limit must be between 1 and {limits.MaxReadLimit}.");
        if (request.Order is { Count: > 128 }) throw new ArgumentException("order cannot contain more than 128 entries.");
        if (request.After is { Length: > 4096 }) throw new ArgumentException("after cursor exceeds the maximum length.");
        var filterCount = 0;
        return new ReadIntent(request.RootEntity, selections,
            request.Filter is null ? null : MapFilter(request.Filter, 1, ref filterCount, limits),
            request.Order?.Select(MapOrder).ToArray(), effectiveLimit, request.Offset, request.After);
    }

    private static ReadSelection MapSelection(FoundgineGatewayReadSelection? value, int depth, ref int count, FoundgineGatewayHttpOptions limits)
    {
        if (value is null) throw new ArgumentException("Selection entries cannot be null.");
        if (depth > limits.MaxSelectionDepth || ++count > limits.MaxSelectionCount) throw new ArgumentException("Read selection limits exceeded.");
        var field = !string.IsNullOrWhiteSpace(value.Field);
        var relationship = !string.IsNullOrWhiteSpace(value.Relationship);
        if (field == relationship) throw new ArgumentException("Each selection must specify exactly one of field or relationship.");
        ReadSelection[]? children = null;
        if (value.Children is not null)
        {
            children = new ReadSelection[value.Children.Count];
            for (var i = 0; i < children.Length; i++)
                children[i] = MapSelection(value.Children[i], depth + 1, ref count, limits);
        }
        return new ReadSelection(field ? value.Field : null, relationship ? value.Relationship : null, children);
    }

    private static ReadFilter MapFilter(FoundgineGatewayReadFilter? value, int depth, ref int count, FoundgineGatewayHttpOptions limits)
    {
        if (value is null) throw new ArgumentException("Filter entries cannot be null.");
        if (depth > limits.MaxFilterDepth || ++count > limits.MaxFilterNodes) throw new ArgumentException("Read filter limits exceeded.");
        return value.Kind?.Trim().ToLowerInvariant() switch
        {
            "field" => new ReadFieldFilter(Required(value.Field, "filter.field"), ParseEnum<SemanticFilterOperator>(value.Operator, "filter.operator"), Normalize(value.Value, 0, limits)),
            "relationship" => new ReadRelationshipFilter(Required(value.Relationship, "filter.relationship"), ParseEnum<SemanticRelationshipQuantifier>(value.Quantifier, "filter.quantifier"), value.Predicate is null ? throw new ArgumentException("Relationship filter requires predicate.") : MapFilter(value.Predicate, depth + 1, ref count, limits)),
            "and" => new ReadAndFilter(MapExpressions(value.Expressions, depth, ref count, limits)),
            "or" => new ReadOrFilter(MapExpressions(value.Expressions, depth, ref count, limits)),
            _ => throw new ArgumentException("Unsupported filter kind.")
        };
    }

    private static ReadFilter[] MapExpressions(List<FoundgineGatewayReadFilter>? values, int depth, ref int count, FoundgineGatewayHttpOptions limits)
    {
        if (values is not { Count: > 0 }) throw new ArgumentException("AND/OR filters require expressions.");
        var result = new ReadFilter[values.Count];
        for (var i = 0; i < values.Count; i++) result[i] = MapFilter(values[i], depth + 1, ref count, limits);
        return result;
    }

    private static ReadOrder MapOrder(FoundgineGatewayReadOrder? value) => value is null ? throw new ArgumentException("Order entries cannot be null.") : new(
        Required(value.Field, "order.field"), ParseEnum<SemanticSortDirection>(value.Direction, "order.direction"),
        value.RelationshipPath, string.IsNullOrWhiteSpace(value.Aggregate) ? SemanticOrderAggregate.None : ParseEnum<SemanticOrderAggregate>(value.Aggregate, "order.aggregate"));

    private static object? Normalize(JsonElement value, int depth, FoundgineGatewayHttpOptions limits)
    {
        if (depth > limits.MaxJsonValueDepth) throw new ArgumentException("JSON value depth exceeded.");
        return value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var i) => i,
            JsonValueKind.Number when value.TryGetDecimal(out var d) => d,
            JsonValueKind.Array => value.EnumerateArray().Select(x => Normalize(x, depth + 1, limits)).ToArray(),
            JsonValueKind.Object => value.EnumerateObject().ToDictionary(x => x.Name, x => Normalize(x.Value, depth + 1, limits)),
            _ => throw new ArgumentException("Unsupported JSON filter value.")
        };
    }

    private static void ValidateLimits(FoundgineGatewayHttpOptions limits)
    {
        if (limits.MaxRequestBytes < 1 || limits.MaxSelectionDepth < 1 || limits.MaxSelectionCount < 1 ||
            limits.MaxFilterDepth < 1 || limits.MaxFilterNodes < 1 || limits.MaxJsonValueDepth < 1 ||
            limits.DefaultReadLimit < 1 || limits.MaxReadLimit < limits.DefaultReadLimit)
            throw new InvalidOperationException("Gateway HTTP limits must all be positive.");
    }

    private static T ParseEnum<T>(string? value, string name) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw new ArgumentException($"Invalid {name}.");
    private static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"{name} is required.");
}

/// <summary>HTTP input bounds. Hosts should also configure Kestrel's global request-body limits.</summary>
public sealed class FoundgineGatewayHttpOptions
{
    public long MaxRequestBytes { get; set; } = 256 * 1024;
    public int DefaultReadLimit { get; set; } = 100;
    public int MaxReadLimit { get; set; } = 1000;
    public int MaxSelectionDepth { get; set; } = 8;
    public int MaxSelectionCount { get; set; } = 128;
    public int MaxFilterDepth { get; set; } = 8;
    public int MaxFilterNodes { get; set; } = 128;
    public int MaxJsonValueDepth { get; set; } = 8;
    public int MaxMutationOperations { get; set; } = 32;
    public int MaxMutationFields { get; set; } = 128;
    public int MaxMutationFilterDepth { get; set; } = 8;
    public int MaxMutationFilterNodes { get; set; } = 128;
}
