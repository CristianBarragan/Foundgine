using Foundgine.Core.Semantic.Query;

namespace Foundgine.Core.Semantic.Security.Execution;

/// <summary>
/// Bounds applied to the protocol-neutral semantic request before resolution and planning.
/// These limits protect the semantic engine even when an adapter other than JSON is used.
/// </summary>
public sealed record SecurityResourceLimits
{
    public int MaxSelectionDepth { get; init; } = 32;
    public int MaxOperationGraphNodes { get; init; } = 256;
    public int MaxOperationGraphDepth { get; init; } = 32;
    public int MaxOperationGraphEdges { get; init; } = 255;
    public int MaxOperationGraphFields { get; init; } = 512;
    public int MaxSelectionNodes { get; init; } = 256;
    public int MaxFilterDepth { get; init; } = 32;
    public int MaxFilterNodes { get; init; } = 256;
    public int MaxOrderTerms { get; init; } = 64;
    public int MaxOrderPathDepth { get; init; } = 16;
    public int MaxPageSize { get; init; } = 1000;
    public int MaxOffset { get; init; } = 1_000_000;
    public int MaxCursorLength { get; init; } = 4096;
    public int MaxMutationOperations { get; init; } = 128;
    public int MaxMutationFieldsPerOperation { get; init; } = 64;
    public int MaxMutationReturnFieldsPerOperation { get; init; } = 64;
    public int MaxMutationDependencies { get; init; } = 256;
    public int MaxMutationEffects { get; init; } = 256;

    public void Validate()
    {
        if (MaxSelectionDepth < 1 || MaxOperationGraphNodes < 1 || MaxOperationGraphDepth < 1 ||
            MaxOperationGraphEdges < 1 || MaxOperationGraphFields < 1 || MaxSelectionNodes < 1 ||
            MaxFilterDepth < 1 || MaxFilterNodes < 1 ||
            MaxOrderTerms < 1 || MaxOrderPathDepth < 1 ||
            MaxPageSize < 1 || MaxOffset < 0 || MaxCursorLength < 1 ||
            MaxMutationOperations < 1 || MaxMutationFieldsPerOperation < 1 ||
            MaxMutationReturnFieldsPerOperation < 1 || MaxMutationDependencies < 1 ||
            MaxMutationEffects < 1)
            throw new ArgumentOutOfRangeException(nameof(SecurityResourceLimits),
                "All security resource limits must be positive, except MaxOffset which may be zero.");
    }
}

/// <summary>
/// Enforces resource and complexity bounds at the semantic boundary.
/// Adapter-level limits are defense-in-depth; this validator is the canonical
/// engine-side guard and therefore also protects non-JSON callers.
/// </summary>
public static class SecurityResourceLimitValidator
{
    public static void Validate(SemanticRequest request, SecurityResourceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        var selectionNodes = CountSelections(request.Selections, 1, limits);
        if (selectionNodes > limits.MaxSelectionNodes)
            Reject($"Selection complexity exceeds the configured maximum of {limits.MaxSelectionNodes} nodes.");

        var options = request.Options;
        if (options is null)
            return;

        if (options.Limit is < 0 || options.Offset is < 0)
            Reject("Pagination values cannot be negative.");
        if (options.Limit is > 0 && options.Limit.Value > limits.MaxPageSize)
            Reject($"Requested page size exceeds the configured maximum of {limits.MaxPageSize}.");
        if (options.Offset is > 0 && options.Offset.Value > limits.MaxOffset)
            Reject($"Requested offset exceeds the configured maximum of {limits.MaxOffset}.");
        if (options.After is not null && options.After.Length > limits.MaxCursorLength)
            Reject($"Cursor length exceeds the configured maximum of {limits.MaxCursorLength}.");

        if (options.EffectiveOrder.Count > limits.MaxOrderTerms)
            Reject($"Order complexity exceeds the configured maximum of {limits.MaxOrderTerms} terms.");

        foreach (var term in options.EffectiveOrder)
        {
            if (term.EffectivePath.Count > limits.MaxOrderPathDepth)
                Reject($"Order relationship path exceeds the configured maximum of {limits.MaxOrderPathDepth} levels.");
        }

        if (options.Filter is not null)
        {
            var filterNodes = CountFilter(options.Filter, 1, limits);
            if (filterNodes > limits.MaxFilterNodes)
                Reject($"Filter complexity exceeds the configured maximum of {limits.MaxFilterNodes} nodes.");
        }
    }

    private static int CountSelections(
        IReadOnlyList<SemanticSelection> selections,
        int depth,
        SecurityResourceLimits limits)
    {
        if (depth > limits.MaxSelectionDepth)
            Reject($"Selection depth exceeds the configured maximum of {limits.MaxSelectionDepth}.");

        var count = 0;
        foreach (var selection in selections)
        {
            count++;
            if (count > limits.MaxSelectionNodes)
                return count;

            if (selection.Children is { Count: > 0 })
            {
                count += CountSelections(selection.Children, depth + 1, limits);
                if (count > limits.MaxSelectionNodes)
                    return count;
            }
        }

        return count;
    }

    public static void ValidateFilter(SemanticFilterExpression filter, SecurityResourceLimits limits) =>
        _ = CountFilter(filter, 1, limits);

    private static int CountFilter(
        SemanticFilterExpression filter,
        int depth,
        SecurityResourceLimits limits)
    {
        if (depth > limits.MaxFilterDepth)
            Reject($"Filter depth exceeds the configured maximum of {limits.MaxFilterDepth}.");

        var count = 1;
        switch (filter)
        {
            case SemanticRelationshipFilter relationship:
                count += CountFilter(relationship.Predicate, depth + 1, limits);
                break;
            case SemanticAggregateFilter aggregate when aggregate.Predicate is not null:
                count += CountFilter(aggregate.Predicate, depth + 1, limits);
                break;
            case SemanticAndFilter and:
                foreach (var expression in and.Expressions)
                    count += CountFilter(expression, depth + 1, limits);
                break;
            case SemanticOrFilter or:
                foreach (var expression in or.Expressions)
                    count += CountFilter(expression, depth + 1, limits);
                break;
        }

        return count > limits.MaxFilterNodes ? count : count;
    }

    private static void Reject(string message) => throw new InvalidOperationException(message);
}