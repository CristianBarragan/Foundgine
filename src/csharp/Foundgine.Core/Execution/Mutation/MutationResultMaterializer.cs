using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic;

namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Shapes a flat mutation batch result back into the semantic nested mutation
/// tree. Provider details and operation indexes remain internal to the result;
/// callers consume entity/field values and relationship children.
/// </summary>
public sealed class MutationResultMaterializer
{
    private readonly SemanticModel _model;

    public MutationResultMaterializer(SemanticModel model) =>
        _model = model ?? throw new ArgumentNullException(nameof(model));

    public MutationMaterializedResult Materialize(
        NestedMutationIntent intent,
        MutationBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(result);

        var expectedOperations = Count(intent);
        if (result.Results.Count != expectedOperations)
        {
            throw new InvalidOperationException(
                $"Mutation result contains {result.Results.Count} operations, but the nested mutation contains {expectedOperations}.");
        }

        var nextOperation = 0;
        var roots = new List<MutationMaterializedNode>(1);
        AddNode(intent, result.Results, ref nextOperation, roots);

        if (nextOperation != result.Results.Count)
            throw new InvalidOperationException("Nested mutation result did not consume every operation result.");

        return new MutationMaterializedResult(roots);
    }

    private void AddNode(
        NestedMutationIntent intent,
        IReadOnlyList<MutationResult> results,
        ref int operationIndex,
        List<MutationMaterializedNode> siblings)
    {
        var currentIndex = operationIndex++;
        var entity = _model.Get(intent.Mutation.Entity);
        var result = results[currentIndex];
        var values = result.ReturnedValues is null
            ? new Dictionary<FieldId, object?>()
            : new Dictionary<FieldId, object?>(result.ReturnedValues);

        var node = new MutationMaterializedNode(currentIndex, entity.Id, values);
        siblings.Add(node);

        foreach (var child in intent.Children)
        {
            var relationship = entity.Relationships.FirstOrDefault(x => x.Id == child.Relationship)
                               ?? throw new InvalidOperationException(
                                   $"Entity '{entity.Name}' has no relationship '{child.Relationship.Value}'.");

            if (relationship.Target != child.Mutation.Mutation.Entity)
            {
                throw new InvalidOperationException(
                    $"Relationship '{relationship.Name}' targets '{relationship.Target.Value}', " +
                    $"but nested result targets '{child.Mutation.Mutation.Entity.Value}'.");
            }

            AddNode(
                child.Mutation,
                results,
                ref operationIndex,
                node.GetChildren(relationship.Id));
        }
    }

    private static int Count(NestedMutationIntent intent) =>
        1 + intent.Children.Sum(x => Count(x.Mutation));

    /// <summary>
    /// Batch form of <see cref="Materialize"/>: takes N independent (key, intent) pairs -
    /// e.g. one per aliased root field from HotChocolateMutationAdapter.AdaptBatchWithResultShape
    /// combined into one plan via MutationPlanner.Plan(IReadOnlyList&lt;NestedMutationIntent&gt;)
    /// - and ONE flat MutationBatchResult produced by executing that combined plan. Slices
    /// `result.Results` back into per-item chunks (same order the planner concatenated them
    /// in) and materializes each independently, so nested children within one item still
    /// resolve correctly.
    /// </summary>
    public IReadOnlyList<(string Key, MutationMaterializedResult Result)> MaterializeBatch(
        IReadOnlyList<(string Key, NestedMutationIntent Intent)> items,
        MutationBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(result);
        if (items.Count == 0)
            throw new InvalidOperationException("A materialized mutation batch must contain at least one item.");

        var output = new List<(string, MutationMaterializedResult)>(items.Count);
        var offset = 0;
        foreach (var (key, intent) in items)
        {
            var count = Count(intent);
            if (offset + count > result.Results.Count)
            {
                throw new InvalidOperationException(
                    $"Batched mutation result has {result.Results.Count} operations total, " +
                    $"but item '{key}' alone needs operations {offset}..{offset + count - 1}.");
            }

            var slice = new MutationBatchResult(result.Results.Skip(offset).Take(count).ToList());
            output.Add((key, Materialize(intent, slice)));
            offset += count;
        }

        if (offset != result.Results.Count)
        {
            throw new InvalidOperationException(
                $"Batched mutation result contains {result.Results.Count} operations, " +
                $"but the batch items only account for {offset}.");
        }

        return output;
    }
}