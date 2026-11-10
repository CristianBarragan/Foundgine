using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Security;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Orm;

/// <summary>
/// Executes a provider-neutral mutation batch through the ORM's unit of work. The whole batch runs in one
/// transaction (or the caller's ambient one); any failure aborts that transaction, including an ambient
/// transaction whose commit ownership remains with the caller. Each operation is flushed
/// before the next so database-generated keys can be referenced by later operations
/// (<see cref="MutationFieldValue.Source"/>). Authorization is carried by <see cref="MutationOperation.Filter"/>
/// as resolved by the mutation planner; update/delete without a filter are refused, never widened.
/// </summary>
public sealed class OrmMutationExecutionProvider : IMutationBatchExecutionProvider, IMutationSecurityConformanceEvaluator
{
    private readonly string _provider;
    private readonly IOrmMutationSource _source;
    private readonly OrmRegistry _registry;

    public OrmMutationExecutionProvider(string provider, IOrmMutationSource source, OrmRegistry registry)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Only guarantees this execution path actually establishes: values are bound as parameters by the
    /// ORM, and the batch is one transaction. Anything else a plan requires is reported as unmet.</summary>
    public MutationSecurityConformanceResult Evaluate(ExecutionMutationIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var violations = new List<string>();
        try { Prepare(ir); }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            violations.Add(ex.Message);
        }

        return new MutationSecurityConformanceResult(
            GetType().FullName ?? GetType().Name,
            [SecurityInvariantIds.ParameterizedValues, SecurityInvariantIds.AtomicMutation],
            violations);
    }

    public MutationBatchResult ExecuteBatch(ExecutionMutationIR ir, ExecutionContext context) =>
        ExecuteBatch(ir, context, CancellationToken.None);

    public MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(context);

        var maps = Prepare(ir); // validates every operation before any database work
        var results = new List<MutationResult>(ir.Operations.Count);

        using var transaction = _source.BeginTransaction();
        try
        {
            for (var i = 0; i < ir.Operations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.EnsureWithinDeadline();

                var op = ir.Operations[i];
                var map = maps[i];
                var values = new List<KeyValuePair<FieldId, object?>>(op.Fields.Count);
                foreach (var field in op.Fields)
                {
                    var fieldId = map.FieldForColumn(field.Column)
                        ?? throw new InvalidOperationException(
                            $"Mutation column '{field.Column.Value}' is not a mapped field of '{map.EntityName}'.");
                    values.Add(new(fieldId, field.Source is null ? field.Value : Resolve(field.Source, results, i)));
                }

                results.Add(map.ApplyMutation(op.Kind, values, op.Filter, ConflictFields(op, map), op.ReturnFields, _source));
            }

            transaction.Commit();
        }
        catch (Exception executionError)
        {
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "The ORM mutation batch failed and its transaction could not be rolled back cleanly.",
                    executionError,
                    rollbackError);
            }

            throw;
        }

        return new MutationBatchResult(results);
    }

    private IOrmEntityMap[] Prepare(ExecutionMutationIR ir)
    {
        ir.ValidateDerivedDependencies();
        var maps = new IOrmEntityMap[ir.Operations.Count];
        for (var i = 0; i < ir.Operations.Count; i++)
        {
            var op = ir.Operations[i];
            var map = _registry.GetMap(op.Entity.Id);
            var fields = op.Fields.Select(f => map.FieldForColumn(f.Column)
                ?? throw new InvalidOperationException(
                    $"Mutation column '{f.Column.Value}' is not a mapped field of '{map.EntityName}'.")).ToArray();
            map.ValidateMutation(op.Kind, fields, op.Filter, ConflictFields(op, map), op.ReturnFields);
            maps[i] = map;
        }

        return maps;
    }

    private static IReadOnlyList<FieldId>? ConflictFields(MutationOperation op, IOrmEntityMap map) =>
        op.ConflictColumns?.Select(c => map.FieldForColumn(c)
            ?? throw new InvalidOperationException(
                $"Conflict column '{c.Value}' is not a mapped field of '{map.EntityName}'.")).ToArray();

    private static object? Resolve(MutationValueReference source, List<MutationResult> results, int current)
    {
        if (source.SourceOperationIndex < 0 || source.SourceOperationIndex >= current)
            throw new InvalidOperationException(
                $"Mutation value reference points to operation {source.SourceOperationIndex} which has not run.");
        var returned = results[source.SourceOperationIndex].ReturnedValues;
        if (returned is null || !returned.TryGetValue(source.SourceField, out var value))
            throw new InvalidOperationException(
                $"Operation {source.SourceOperationIndex} did not return field {source.SourceField.Value}; " +
                "add it to ReturnFields.");
        return value;
    }
}
