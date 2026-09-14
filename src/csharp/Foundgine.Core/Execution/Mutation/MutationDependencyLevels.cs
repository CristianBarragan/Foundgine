using Foundgine.Core.Semantic.Planning.Mutation;

namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Computes execution levels directly from canonical mutation dependencies.
/// No correlation-shaped compatibility model is involved.
/// </summary>
public static class MutationDependencyLevels
{
    public static IReadOnlyList<IReadOnlyList<int>> Compute(
        int operationCount,
        IEnumerable<MutationDependency> dependencies)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(operationCount);
        ArgumentNullException.ThrowIfNull(dependencies);

        var edges = dependencies.ToArray();
        foreach (var dependency in edges)
        {
            if (dependency.SourceOperationIndex < 0 ||
                dependency.SourceOperationIndex >= operationCount ||
                dependency.TargetOperationIndex < 0 ||
                dependency.TargetOperationIndex >= operationCount)
                throw new InvalidOperationException(
                    $"Mutation dependency indexes are outside the execution graph: " +
                    $"{dependency.SourceOperationIndex} -> {dependency.TargetOperationIndex}.");

            if (dependency.SourceOperationIndex == dependency.TargetOperationIndex)
                throw new InvalidOperationException(
                    $"Mutation dependency cycle detected at operation {dependency.SourceOperationIndex}.");
        }

        if (operationCount == 0)
            return Array.Empty<IReadOnlyList<int>>();

        var incoming = Enumerable.Range(0, operationCount)
            .ToDictionary(i => i, _ => 0);
        var outgoing = Enumerable.Range(0, operationCount)
            .ToDictionary(i => i, _ => new List<int>());

        foreach (var dependency in edges)
        {
            if (!outgoing[dependency.SourceOperationIndex].Contains(dependency.TargetOperationIndex))
            {
                outgoing[dependency.SourceOperationIndex].Add(dependency.TargetOperationIndex);
                incoming[dependency.TargetOperationIndex]++;
            }
        }

        var remaining = Enumerable.Range(0, operationCount).ToHashSet();
        var levels = new List<IReadOnlyList<int>>();

        while (remaining.Count > 0)
        {
            var level = remaining
                .Where(i => incoming[i] == 0)
                .OrderBy(i => i)
                .ToArray();

            if (level.Length == 0)
                throw new InvalidOperationException(
                    "Mutation dependency graph contains a cycle.");

            levels.Add(level);

            foreach (var node in level)
            {
                remaining.Remove(node);
                foreach (var target in outgoing[node])
                    incoming[target]--;
            }
        }

        return levels;
    }
}
