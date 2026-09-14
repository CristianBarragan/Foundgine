using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Storage.InMemory;

/// <summary>A small provider-neutral test/runtime provider backed by CLR objects.</summary>
public sealed record InMemoryRow(
    EntityId EntityId,
    IReadOnlyDictionary<FieldId, object?> Values);

public sealed class InMemoryDataSet
{
    private readonly Dictionary<EntityId, List<InMemoryRow>> _rows = [];

    public InMemoryDataSet Add(InMemoryRow row)
    {
        if (!_rows.TryGetValue(row.EntityId, out var rows))
            _rows[row.EntityId] = rows = [];
        rows.Add(row);
        return this;
    }

    internal IReadOnlyList<InMemoryRow> Get(EntityId entityId) =>
        _rows.TryGetValue(entityId, out var rows) ? rows : [];
}

public sealed record InMemoryPlan(ExecutionIR IR) : ProviderPlan("in-memory");

/// <summary>
/// Executes the provider-neutral execution IR directly over CLR data. It deliberately
/// has no SQL dependency and uses metadata only to resolve relationships.
/// This provider is intentionally small: its purpose is to prove that the
/// execution plan is not merely SQL with the SQL removed.
/// </summary>
public sealed class InMemoryExecutionProvider : IExecutionProvider
{
    private readonly InMemoryCompiler _compiler;

    public InMemoryExecutionProvider(IMetadataProvider metadata, InMemoryDataSet data)
    {
        _compiler = new InMemoryCompiler(metadata, data);
    }

    public Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken = default) =>
        _compiler.ExecuteAsync(plan, context, cancellationToken);
}

public sealed class InMemoryCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
    IProviderSecurityConformanceEvaluator
{
    private readonly IMetadataProvider? _metadata;
    private readonly InMemoryDataSet? _data;

    public InMemoryCompiler()
    {
    }

    public IReadOnlyCollection<string> PreservedSecurityInvariants =>
    [
        SecurityInvariantIds.AuthorizationRequired,
        SecurityInvariantIds.RuntimeAuthorization,
        SecurityInvariantIds.FieldVisibility,
        SecurityInvariantIds.RelationshipVisibility,
        SecurityInvariantIds.ParameterizedValues,
        SecurityInvariantIds.PlanCacheContextIsolation
    ];

    public InMemoryCompiler(IMetadataProvider metadata, InMemoryDataSet data)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan is not InMemoryPlan memoryPlan)
            throw new ArgumentException("Expected an InMemoryPlan.", nameof(plan));

        var required = ir.RequiredSecurityInvariants
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var satisfied = required.Intersect(PreservedSecurityInvariants, StringComparer.Ordinal).ToArray();
        var violations = required.Except(satisfied, StringComparer.Ordinal)
            .Select(x => $"Invariant '{x}' is not certified by the in-memory execution plan.")
            .ToArray();

        // The concrete plan must carry the same IR that was certified.
        if (!ReferenceEquals(memoryPlan.IR, ir))
            violations = violations.Append("The certified InMemoryPlan does not reference the supplied ExecutionIR.")
                .ToArray();

        return new ProviderSecurityConformanceResult(
            memoryPlan.Provider, required, satisfied, violations);
    }

    /// <summary>Compatibility bridge for callers holding a semantic plan.
    /// Lowering remains explicit and provider-neutral.</summary>
    public ProviderPlan Compile(SemanticPlan plan) => Compile(ExecutionIRCompiler.Compile(plan));

    public ProviderPlan Compile(ExecutionIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var plan = new InMemoryPlan(ir);
        ExecutionIRBoundary.BindProviderPlan(ir, plan);
        return plan;
    }


    public Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (plan is not InMemoryPlan memoryPlan)
            throw new ArgumentException("Expected an InMemoryPlan.", nameof(plan));
        if (memoryPlan.AuthorizationBinding is null)
            throw new InvalidOperationException(
                "The in-memory provider refuses to execute a provider plan without authorization provenance.");

        cancellationToken.ThrowIfCancellationRequested();
        if (_metadata is null || _data is null)
            throw new InvalidOperationException(
                "InMemoryCompiler execution requires metadata and data. Construct it with InMemoryCompiler(metadata, data).");
        var rows = ExecuteNode(memoryPlan.IR.Root, context, cancellationToken, null, isRoot: true).ToList();
        return Task.FromResult(new ExecutionResult(rows));
    }

    private IEnumerable<ExecutionRow> ExecuteNode(
        ExecutionIRNode node,
        ExecutionContext context,
        CancellationToken cancellationToken,
        InMemoryRow? parent,
        bool isRoot)
    {
        var data = _data ?? throw new InvalidOperationException("InMemoryCompiler requires data for execution.");
        IEnumerable<InMemoryRow> candidates = parent is null
            ? data.Get(node.EntityId)
            : Traverse(parent, node);

        if (node.Authorization is not null)
            candidates = candidates.Where(row => EvaluateAuthorization(node.Authorization, row, context));

        if (isRoot)
            candidates = ApplyQueryOptions(candidates, node.QueryOptions, node.EntityId);

        foreach (var row in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childRows = new List<IReadOnlyList<ExecutionRow>>();
            foreach (var child in node.Children)
                childRows.Add(ExecuteNode(child, context, cancellationToken, row, isRoot: false).ToArray());

            if (node.Children.Count == 0)
            {
                yield return ToExecutionRow(node, row);
                continue;
            }

            var combinations = Cartesian(childRows);
            foreach (var combination in combinations)
            {
                var merged = new List<ExecutionRow> { ToExecutionRow(node, row) };
                merged.AddRange(combination);
                yield return MergeRows(merged);
            }
        }
    }

    private IEnumerable<InMemoryRow> Traverse(InMemoryRow parent, ExecutionIRNode child)
    {
        if (child.ViaRelationship is not { } relationshipId)
            throw new NotSupportedException("The in-memory provider currently supports relationship traversal only.");

        var metadata = _metadata ??
                       throw new InvalidOperationException("InMemoryCompiler requires metadata for execution.");
        var data = _data ?? throw new InvalidOperationException("InMemoryCompiler requires data for execution.");
        var source = metadata.GetRelationship(relationshipId);
        var sourceField = FieldForColumn(source.Source, source.SourceKey.ColumnId);
        var targetField = FieldForColumn(source.Target, source.TargetKey.ColumnId);
        var parentValue = parent.Values.TryGetValue(sourceField, out var value) ? value : null;

        return data.Get(child.EntityId).Where(row =>
            row.Values.TryGetValue(targetField, out var childValue) && Equals(parentValue, childValue));
    }

    private IEnumerable<InMemoryRow> ApplyQueryOptions(
        IEnumerable<InMemoryRow> rows,
        SemanticQueryOptions? options,
        EntityId entityId)
    {
        if (options is null)
            return rows;

        if (options.Filter is not null)
            rows = rows.Where(row => EvaluateFilter(options.Filter, row, entityId));

        IOrderedEnumerable<InMemoryRow>? ordered = null;
        foreach (var term in options.EffectiveOrder.Where(x => x.IsRootField))
        {
            Func<InMemoryRow, object?> key = row => row.Values.TryGetValue(term.Field, out var value) ? value : null;
            ordered = ordered is null
                ? (term.Direction == SemanticSortDirection.Asc
                    ? rows.OrderBy(key, Comparer<object?>.Create(Compare))
                    : rows.OrderByDescending(key, Comparer<object?>.Create(Compare)))
                : (term.Direction == SemanticSortDirection.Asc
                    ? ordered.ThenBy(key, Comparer<object?>.Create(Compare))
                    : ordered.ThenByDescending(key, Comparer<object?>.Create(Compare)));
        }

        rows = ordered ?? rows;

        if (options.Offset is { } offset)
            rows = rows.Skip(offset);
        if (options.Limit is { } limit)
            rows = rows.Take(limit);
        return rows;
    }

    private static bool EvaluateFilter(SemanticFilterExpression filter, InMemoryRow row, EntityId entityId) =>
        filter switch
        {
            SemanticFieldFilter f => CompareValue(row.Values.TryGetValue(f.Field, out var v) ? v : null, f.Operator,
                f.Value),
            SemanticAndFilter a => a.Expressions.All(x => EvaluateFilter(x, row, entityId)),
            SemanticOrFilter o => o.Expressions.Any(x => EvaluateFilter(x, row, entityId)),
            _ => throw new NotSupportedException($"In-memory filter '{filter.GetType().Name}' is not implemented.")
        };

    private static bool CompareValue(object? actual, SemanticFilterOperator op, object? expected) => op switch
    {
        SemanticFilterOperator.Eq => Equals(actual, expected),
        SemanticFilterOperator.Neq => !Equals(actual, expected),
        SemanticFilterOperator.In => expected is System.Collections.IEnumerable values &&
                                     values.Cast<object?>().Any(x => Equals(actual, x)),
        _ => false
    };

    private bool EvaluateAuthorization(AuthorizationPredicate predicate, InMemoryRow row, ExecutionContext context) =>
        predicate.Kind switch
        {
            AuthorizationPredicateKind.Equal => Equals(EvaluateValue(predicate.Left!, row, context),
                EvaluateValue(predicate.Right!, row, context)),
            AuthorizationPredicateKind.NotEqual => !Equals(EvaluateValue(predicate.Left!, row, context),
                EvaluateValue(predicate.Right!, row, context)),
            AuthorizationPredicateKind.And => EvaluateAuthorization(predicate.Left!, row, context) &&
                                              EvaluateAuthorization(predicate.Right!, row, context),
            AuthorizationPredicateKind.Or => EvaluateAuthorization(predicate.Left!, row, context) ||
                                             EvaluateAuthorization(predicate.Right!, row, context),
            AuthorizationPredicateKind.Not => !EvaluateAuthorization(predicate.Left!, row, context),
            _ => Convert.ToBoolean(EvaluateValue(predicate, row, context))
        };

    private object? EvaluateValue(AuthorizationPredicate node, InMemoryRow row, ExecutionContext context)
    {
        return node.Kind switch
        {
            AuthorizationPredicateKind.Constant => ParseConstant(node.Value),
            AuthorizationPredicateKind.ResourceParameter => row,
            AuthorizationPredicateKind.ContextParameter => context.TryGetValue(node.Name ?? "", out var value)
                ? value
                : null,
            AuthorizationPredicateKind.MemberAccess => ReadMember(node, row, context),
            _ when node.Kind is AuthorizationPredicateKind.Equal or AuthorizationPredicateKind.NotEqual
                or AuthorizationPredicateKind.And or AuthorizationPredicateKind.Or
                or AuthorizationPredicateKind.Not => EvaluateAuthorization(node, row, context),
            _ => throw new NotSupportedException($"Authorization node '{node.Kind}' is not implemented.")
        };
    }

    private object? ReadMember(AuthorizationPredicate node, InMemoryRow row, ExecutionContext context)
    {
        var target = node.Left ?? throw new InvalidOperationException("Member access has no target.");
        if (target.Kind == AuthorizationPredicateKind.ResourceParameter)
        {
            var name = node.Name ?? throw new InvalidOperationException("Resource member has no name.");
            var entity =
                (_metadata ?? throw new InvalidOperationException("InMemoryCompiler requires metadata for execution."))
                .GetEntity(row.EntityId);
            var field = entity.EffectiveFields.FirstOrDefault(x => x.Name == name)
                        ?? throw new InvalidOperationException(
                            $"Authorization resource member '{entity.Name}.{name}' has no field mapping.");
            return row.Values.TryGetValue(field.Id, out var value) ? value : null;
        }

        if (target.Kind == AuthorizationPredicateKind.ContextParameter)
        {
            var path = (target.Name ?? "") + "." + (node.Name ?? "");
            return context.TryGetValue(path, out var value) ? value : null;
        }

        throw new NotSupportedException(
            "Only context and resource member authorization is supported by this minimal provider.");
    }

    private ExecutionRow ToExecutionRow(ExecutionIRNode node, InMemoryRow row) =>
        new(
            node.Fields.ToDictionary(
                field => $"__fg_{node.Id}_{field}",
                field => row.Values.TryGetValue(field, out var value) ? value : null),
            node.Fields.ToDictionary(
                field => new ExecutionCellKey(node.Id, node.EntityId, field),
                field => row.Values.TryGetValue(field, out var value) ? value : null));

    private static ExecutionRow MergeRows(IEnumerable<ExecutionRow> rows)
    {
        var list = rows.ToArray();
        return new ExecutionRow(
            list.SelectMany(x => x.Values).GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Last().Value),
            list.SelectMany(x => x.EffectiveCells).GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Last().Value));
    }

    private static IEnumerable<IReadOnlyList<ExecutionRow>> Cartesian(IReadOnlyList<IReadOnlyList<ExecutionRow>> groups)
    {
        if (groups.Count == 0)
        {
            yield return [];
            yield break;
        }

        IEnumerable<IReadOnlyList<ExecutionRow>> current = [[]];
        foreach (var group in groups)
            current = current.SelectMany(prefix => group.Select(item => prefix.Append(item).ToArray()));
        foreach (var result in current) yield return result;
    }

    private static int Compare(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        if (left is IComparable comparable) return comparable.CompareTo(right);
        return string.CompareOrdinal(left.ToString(), right.ToString());
    }

    private static object? ParseConstant(string? value) => value switch
    {
        null or "null" => null,
        "true" => true,
        "false" => false,
        _ when int.TryParse(value, out var i) => i,
        _ when long.TryParse(value, out var l) => l,
        _ when decimal.TryParse(value, out var d) => d,
        _ => value
    };

    private FieldId FieldForColumn(EntityId entityId, ColumnId columnId)
    {
        var entity =
            (_metadata ?? throw new InvalidOperationException("InMemoryCompiler requires metadata for execution."))
            .GetEntity(entityId);
        return entity.EffectiveFields.First(x => x.Column?.ColumnId == columnId).Id;
    }
}