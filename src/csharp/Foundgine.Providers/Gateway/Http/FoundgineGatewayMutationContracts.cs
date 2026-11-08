using System.Text.Json;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Providers.Gateway.Http;

/// <summary>Bounded wire representation of the supported canonical mutation graph subset.</summary>
public sealed class FoundgineGatewayMutationRequest
{
    public List<FoundgineGatewayMutationOperation>? Operations { get; set; }
}

public sealed class FoundgineGatewayMutationOperation
{
    public ulong EntityId { get; set; }
    public string? Kind { get; set; }
    public Dictionary<string, JsonElement>? Fields { get; set; }
    public List<ulong>? ConflictFields { get; set; }
    public List<ulong>? ReturnFields { get; set; }
    public FoundgineGatewayMutationFilter? Filter { get; set; }
}

public sealed class FoundgineGatewayMutationFilter
{
    public string? Kind { get; set; }
    public ulong? FieldId { get; set; }
    public string? Operator { get; set; }
    public JsonElement Value { get; set; }
    public List<FoundgineGatewayMutationFilter>? Expressions { get; set; }
}

public sealed record FoundgineGatewayMutationDryRunResponse(
    string PlanFingerprint, IReadOnlyList<FoundgineGatewayMutationPlanOperation> Operations,
    IReadOnlyList<string> Effects);
public sealed record FoundgineGatewayMutationPlanOperation(
    int Index, string Entity, string Kind, IReadOnlyList<string> Fields, IReadOnlyList<string> ReturnFields);
public sealed record FoundgineGatewayApprovalResponse(string Handle, DateTimeOffset ExpiresAt, string PlanFingerprint);
public sealed record FoundgineGatewayMutationExecutionResponse(
    string PlanFingerprint, string ResultFingerprint, string? ApprovalId, string? ApprovedBy,
    int OperationCount, int TotalAffectedRows);

internal static class FoundgineGatewayMutationRequestMapper
{
    public static SemanticMutationOperationGraph ToGraph(FoundgineGatewayMutationRequest request, FoundgineGatewayHttpOptions limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Operations is not { Count: > 0 } operations || operations.Count > limits.MaxMutationOperations)
            throw new ArgumentException("Mutation operations must be non-empty and within the configured limit.");
        var mapped = new List<SemanticMutationOperation>(operations.Count);
        foreach (var operation in operations)
        {
            if (operation is null || operation.EntityId == 0) throw new ArgumentException("Every mutation requires a non-zero entityId.");
            var fields = new List<SemanticMutationField>();
            var seenFieldIds = new HashSet<ulong>();
            if (operation.Fields is not null)
            {
                if (operation.Fields.Count > limits.MaxMutationFields) throw new ArgumentException("Mutation field limit exceeded.");
                foreach (var pair in operation.Fields)
                {
                    if (!ulong.TryParse(pair.Key, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var fieldId) || fieldId == 0)
                        throw new ArgumentException("Mutation field keys must be non-zero numeric semantic field IDs.");
                    if (!seenFieldIds.Add(fieldId)) throw new ArgumentException("Mutation field IDs must be unique.");
                    fields.Add(new SemanticMutationField(new FieldId(fieldId), Normalize(pair.Value, 0, limits)));
                }
            }
            var entity = new EntityId(operation.EntityId);
            var returns = (operation.ReturnFields ?? []).Select(x => x == 0 ? throw new ArgumentException("Return field IDs must be non-zero.") : new FieldId(x)).ToArray();
            var conflicts = (operation.ConflictFields ?? []).Select(x => x == 0 ? throw new ArgumentException("Conflict field IDs must be non-zero.") : new FieldId(x)).ToArray();
            var kind = Parse<SemanticMutationKind>(operation.Kind, "mutation kind");
            if (returns.Length > limits.MaxMutationFields || conflicts.Length > limits.MaxMutationFields)
                throw new ArgumentException("Mutation return/conflict field limit exceeded.");
            var filterNodes = 0;
            var filter = operation.Filter is null ? null : MapFilter(operation.Filter, 1, ref filterNodes, limits);
            if ((kind is SemanticMutationKind.Update or SemanticMutationKind.Delete) && filter is null)
                throw new ArgumentException($"{kind} mutations require a target filter.");
            if (kind == SemanticMutationKind.Upsert && conflicts.Length == 0)
                throw new ArgumentException("Upsert mutations require conflictFields.");
            if ((kind is SemanticMutationKind.Create or SemanticMutationKind.Upsert) && filter is not null)
                throw new ArgumentException($"{kind} mutations do not accept a target filter.");
            if (kind != SemanticMutationKind.Upsert && conflicts.Length != 0)
                throw new ArgumentException("conflictFields are supported only for Upsert.");
            if (kind == SemanticMutationKind.Delete && fields.Count != 0)
                throw new ArgumentException("Delete mutations do not accept field assignments.");
            mapped.Add(kind switch
            {
                SemanticMutationKind.Create => SemanticMutationBuilder.Create(entity, fields, returns),
                SemanticMutationKind.Update => SemanticMutationBuilder.Update(entity, fields, filter, returns),
                SemanticMutationKind.Delete => SemanticMutationBuilder.Delete(entity, filter ?? throw new ArgumentException("Delete requires a filter."), returns),
                SemanticMutationKind.Upsert => SemanticMutationBuilder.Upsert(entity, fields, conflicts, returns),
                _ => throw new ArgumentException("Unsupported mutation kind.")
            });
        }
        return new SemanticMutationOperationGraph(mapped);
    }

    private static SemanticFilterExpression MapFilter(FoundgineGatewayMutationFilter filter, int depth, ref int count, FoundgineGatewayHttpOptions limits)
    {
        if (depth > limits.MaxMutationFilterDepth || ++count > limits.MaxMutationFilterNodes)
            throw new ArgumentException("Mutation filter depth/node limit exceeded.");
        return filter.Kind?.Trim().ToLowerInvariant() switch
        {
            "field" when filter.FieldId is > 0 => new SemanticFieldFilter(new FieldId(filter.FieldId.Value),
                Parse<SemanticFilterOperator>(filter.Operator, "filter operator"), Normalize(filter.Value, 0, limits)),
            "and" => new SemanticAndFilter(MapExpressions(filter.Expressions, depth, ref count, limits)),
            "or" => new SemanticOrFilter(MapExpressions(filter.Expressions, depth, ref count, limits)),
            _ => throw new ArgumentException("Unsupported mutation filter. Supported kinds are field, and and or.")
        };
    }

    private static IReadOnlyList<SemanticFilterExpression> MapExpressions(List<FoundgineGatewayMutationFilter>? filters, int depth, ref int count, FoundgineGatewayHttpOptions limits)
    {
        if (filters is not { Count: > 0 })
            throw new ArgumentException("AND/OR filters require a non-empty expressions array.");
        var mapped = new SemanticFilterExpression[filters.Count];
        for (var i = 0; i < filters.Count; i++) mapped[i] = MapFilter(filters[i], depth + 1, ref count, limits);
        return mapped;
    }

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
            _ => throw new ArgumentException("Unsupported JSON mutation value.")
        };
    }

    private static T Parse<T>(string? value, string name) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw new ArgumentException($"Invalid {name}.");
}
