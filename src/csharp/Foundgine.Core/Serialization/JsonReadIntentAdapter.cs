using System.Text.Json;
using System.Text.Json.Serialization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Serialization;

/// <summary>
/// Translates a deliberately small JSON representation into Foundgine's
/// provider-neutral <see cref="ReadIntent"/>. It performs no semantic
/// resolution, authorization, planning, or provider work.
/// </summary>
public sealed class JsonReadIntentAdapter
{
    private readonly JsonReadIntentAdapterOptions _limits;

    public JsonReadIntentAdapter(JsonReadIntentAdapterOptions? limits = null)
    {
        _limits = limits ?? new JsonReadIntentAdapterOptions();
        ValidateLimits(_limits);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions PermissiveOptions = new(Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    public ReadIntent Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            var options = _limits.RejectUnknownProperties ? Options : PermissiveOptions;
            var dto = JsonSerializer.Deserialize<ReadIntentDto>(json, options)
                      ?? throw Invalid("JSON document is empty.");
            return ToIntent(dto);
        }
        catch (JsonException ex)
        {
            throw Invalid($"Invalid JSON read intent: {ex.Message}", ex);
        }
    }

    private ReadIntent ToIntent(ReadIntentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RootEntity))
            throw Invalid("'rootEntity' is required.");

        if (dto.Selections is null || dto.Selections.Count == 0)
            throw Invalid("'selections' must contain at least one selection.");

        if (CountSelections(dto.Selections) > _limits.MaxSelections)
            throw Invalid($"Selection count exceeds the configured maximum of {_limits.MaxSelections}.");

        if (dto.Limit is < 0 || dto.Offset is < 0)
            throw Invalid("'limit' and 'offset' cannot be negative.");

        return new ReadIntent(
            dto.RootEntity,
            dto.Selections.Select(ToSelection).ToArray(),
            dto.Filter is null ? null : ToFilter(dto.Filter),
            dto.Order?.Select(ToOrder).ToArray(),
            dto.Limit,
            dto.Offset,
            dto.After);
    }

    private ReadSelection ToSelection(SelectionDto dto, int depth = 1)
    {
        if (depth > _limits.MaxSelectionDepth)
            throw Invalid($"Selection depth exceeds the configured maximum of {_limits.MaxSelectionDepth}.");

        var hasField = !string.IsNullOrWhiteSpace(dto.Field);
        var hasRelationship = !string.IsNullOrWhiteSpace(dto.Relationship);
        if (hasField == hasRelationship)
            throw Invalid("Each selection must specify exactly one of 'field' or 'relationship'.");

        return new ReadSelection(
            hasField ? dto.Field : null,
            hasRelationship ? dto.Relationship : null,
            dto.Children?.Select(child => ToSelection(child, depth + 1)).ToArray());
    }

    private ReadFilter ToFilter(FilterDto dto)
    {
        var nodes = 0;
        return ToFilter(dto, 1, ref nodes);
    }

    private ReadFilter ToFilter(FilterDto dto, int depth, ref int nodes)
    {
        if (depth > _limits.MaxFilterDepth)
            throw Invalid($"Filter depth exceeds the configured maximum of {_limits.MaxFilterDepth}.");
        if (++nodes > _limits.MaxFilterNodes)
            throw Invalid($"Filter node count exceeds the configured maximum of {_limits.MaxFilterNodes}.");
        if (dto.Kind is null)
            throw Invalid("Every filter requires a 'kind'.");

        return dto.Kind.Trim().ToLowerInvariant() switch
        {
            "field" => new ReadFieldFilter(
                Required(dto.Field, "filter.field"),
                dto.Operator ?? throw Invalid("Field filters require 'operator'."),
                ToValue(dto.Value, 0)),

            "relationship" => new ReadRelationshipFilter(
                Required(dto.Relationship, "filter.relationship"),
                dto.Quantifier ?? throw Invalid("Relationship filters require 'quantifier'."),
                dto.Predicate is null
                    ? throw Invalid("Relationship filters require 'predicate'.")
                    : ToFilter(dto.Predicate, depth + 1, ref nodes)),

            "and" => new ReadAndFilter(
                dto.Expressions is { Count: > 0 }
                    ? ToFilterList(dto.Expressions, depth + 1, ref nodes)
                    : throw Invalid("AND filters require at least one expression.")),

            "or" => new ReadOrFilter(
                dto.Expressions is { Count: > 0 }
                    ? ToFilterList(dto.Expressions, depth + 1, ref nodes)
                    : throw Invalid("OR filters require at least one expression.")),

            _ => throw Invalid($"Unsupported filter kind '{dto.Kind}'.")
        };
    }

    // A ref parameter cannot be captured inside a lambda (CS1628), so the
    // recursive per-expression compilation is done via an explicit loop
    // instead of dto.Expressions.Select(...).
    private ReadFilter[] ToFilterList(IReadOnlyList<FilterDto> expressions, int depth, ref int nodes)
    {
        var result = new ReadFilter[expressions.Count];
        for (var i = 0; i < expressions.Count; i++)
            result[i] = ToFilter(expressions[i], depth, ref nodes);
        return result;
    }

    private object? ToValue(JsonElement? value, int depth) => value is null ? null : Normalize(value.Value, depth);

    private object? Normalize(JsonElement value, int depth)
    {
        if (depth > _limits.MaxJsonValueDepth)
            throw Invalid($"JSON value depth exceeds the configured maximum of {_limits.MaxJsonValueDepth}.");

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Array => value.EnumerateArray().Select(item => Normalize(item, depth + 1)).ToArray(),
            JsonValueKind.Object => value.EnumerateObject()
                .ToDictionary(x => x.Name, x => Normalize(x.Value, depth + 1)),
            _ => throw Invalid($"Unsupported JSON value kind '{value.ValueKind}'.")
        };
    }

    private static ReadOrder ToOrder(OrderDto dto) =>
        new(
            Required(dto.Field, "order.field"),
            dto.Direction ?? throw Invalid("Order entries require 'direction'."),
            dto.RelationshipPath,
            dto.Aggregate ?? SemanticOrderAggregate.None);

    private int CountSelections(IEnumerable<SelectionDto> selections) =>
        CountSelections(selections, 1);

    private int CountSelections(IEnumerable<SelectionDto> selections, int depth)
    {
        if (depth > _limits.MaxSelectionDepth)
            throw Invalid($"Selection depth exceeds the configured maximum of {_limits.MaxSelectionDepth}.");

        var count = 0;
        foreach (var selection in selections)
        {
            if (++count > _limits.MaxSelections)
                return count;

            if (selection.Children is not null)
            {
                count += CountSelections(selection.Children, depth + 1);
                if (count > _limits.MaxSelections)
                    return count;
            }
        }

        return count;
    }

    private static void ValidateLimits(JsonReadIntentAdapterOptions limits)
    {
        if (limits.MaxSelectionDepth < 1 || limits.MaxSelections < 1 ||
            limits.MaxFilterDepth < 1 || limits.MaxFilterNodes < 1 ||
            limits.MaxJsonValueDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(limits), "All parser limits must be positive.");
    }

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw Invalid($"'{name}' is required.");

    private static InvalidOperationException Invalid(string message, Exception? inner = null) =>
        new(message, inner);

    private sealed class ReadIntentDto
    {
        public string? RootEntity { get; set; }
        public List<SelectionDto>? Selections { get; set; }
        public FilterDto? Filter { get; set; }
        public List<OrderDto>? Order { get; set; }
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public string? After { get; set; }
    }

    private sealed class SelectionDto
    {
        public string? Field { get; set; }
        public string? Relationship { get; set; }
        public List<SelectionDto>? Children { get; set; }
    }

    private sealed class FilterDto
    {
        public string? Kind { get; set; }
        public string? Field { get; set; }
        public SemanticFilterOperator? Operator { get; set; }
        public JsonElement? Value { get; set; }
        public string? Relationship { get; set; }
        public SemanticRelationshipQuantifier? Quantifier { get; set; }
        public FilterDto? Predicate { get; set; }
        public List<FilterDto>? Expressions { get; set; }
    }

    private sealed class OrderDto
    {
        public string? Field { get; set; }
        public SemanticSortDirection? Direction { get; set; }
        public List<string>? RelationshipPath { get; set; }
        public SemanticOrderAggregate? Aggregate { get; set; }
    }
}