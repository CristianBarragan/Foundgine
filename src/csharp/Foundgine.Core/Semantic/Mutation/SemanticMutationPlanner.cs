using System.Globalization;

namespace Foundgine.Core.Semantic.Mutation;

/// <summary>
/// Canonical semantic mutation planner. It validates and organizes mutation
/// meaning, including value-flow dependencies. Physical schema lowering happens later.
/// </summary>
public sealed class SemanticMutationPlanner
{
    public SemanticMutationPlan Plan(SemanticMutationOperationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.Operations.Count == 0)
            throw new InvalidOperationException(
                "A semantic mutation graph must contain at least one operation.");

        var operations = graph.Operations
            .Select((operation, index) => new SemanticMutationOperationPlan(
                OperationId: Id(index),
                Entity: operation.Entity,
                Kind: operation.Kind,
                Fields: operation.Fields.ToArray(),
                Filter: operation.Filter,
                ConflictFields: operation.ConflictFields.ToArray(),
                ReturnFields: operation.ReturnFields.ToArray(),
                Effects: operation.Effects.ToArray()))
            .ToArray();

        // Field source references are the canonical expression of value flow.
        // Explicit dependency metadata may additionally carry relationship context,
        // so it is merged into the same semantic edge set rather than becoming a
        // second source of truth.
        var dependencyMap = new Dictionary<DependencyKey, SemanticMutationDependencyPlan>();

        for (var targetIndex = 0; targetIndex < graph.Operations.Count; targetIndex++)
        {
            var operation = graph.Operations[targetIndex];
            foreach (var field in operation.Fields)
            {
                if (field.Source is not { } source)
                    continue;

                ValidateSourceIndex(source.SourceOperationIndex, targetIndex, graph.Operations.Count);
                ValidateSourceFieldIsReturned(graph.Operations[source.SourceOperationIndex], source.SourceField,
                    source.SourceOperationIndex, targetIndex);

                var dependency = new SemanticMutationDependencyPlan(
                    Id(source.SourceOperationIndex),
                    Id(targetIndex),
                    source.SourceField,
                    field.Field);

                dependencyMap[new DependencyKey(
                    dependency.FromOperationId,
                    dependency.ToOperationId,
                    dependency.SourceField,
                    dependency.TargetField)] = dependency;
            }

            foreach (var dependency in operation.Dependencies)
            {
                ValidateSourceIndex(dependency.SourceOperationIndex, targetIndex, graph.Operations.Count);
                ValidateSourceFieldIsReturned(graph.Operations[dependency.SourceOperationIndex], dependency.SourceField,
                    dependency.SourceOperationIndex, targetIndex);

                var planned = new SemanticMutationDependencyPlan(
                    Id(dependency.SourceOperationIndex),
                    Id(targetIndex),
                    dependency.SourceField,
                    dependency.TargetField,
                    dependency.Relationship);

                var key = new DependencyKey(
                    planned.FromOperationId,
                    planned.ToOperationId,
                    planned.SourceField,
                    planned.TargetField);

                if (dependencyMap.TryGetValue(key, out var existing) &&
                    existing.Relationship is null &&
                    planned.Relationship is not null)
                {
                    dependencyMap[key] = existing with { Relationship = planned.Relationship };
                }
                else
                {
                    dependencyMap[key] = planned;
                }
            }
        }

        var dependencies = dependencyMap.Values
            .OrderBy(x => int.Parse(x.FromOperationId, CultureInfo.InvariantCulture))
            .ThenBy(x => int.Parse(x.ToOperationId, CultureInfo.InvariantCulture))
            .ThenBy(x => x.SourceField.Value)
            .ThenBy(x => x.TargetField.Value)
            .ToArray();

        // Dependency edges are the canonical semantic representation.
        // Correlation is a property of value-flow dependencies, not a second
        // semantic edge type. Providers introduce a physical carrier only when
        // lowering this plan into execution work.
        return new SemanticMutationPlan(operations, dependencies);
    }

    private static string Id(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static void ValidateSourceIndex(int source, int target, int count)
    {
        if (source < 0 || source >= count || source >= target)
            throw new InvalidOperationException(
                $"Semantic mutation dependency {source} -> {target} is invalid; " +
                "dependencies must point to an earlier operation.");
    }

    private static void ValidateSourceFieldIsReturned(
        SemanticMutationOperation source,
        Foundgine.Core.Abstractions.FieldId sourceField,
        int sourceIndex,
        int targetIndex)
    {
        if (!source.ReturnFields.Contains(sourceField))
            throw new InvalidOperationException(
                $"Semantic mutation dependency {sourceIndex} -> {targetIndex} references field '{sourceField.Value}', " +
                "but the source operation does not return that field.");
    }

    private readonly record struct DependencyKey(
        string FromOperationId,
        string ToOperationId,
        Foundgine.Core.Abstractions.FieldId SourceField,
        Foundgine.Core.Abstractions.FieldId TargetField);
}