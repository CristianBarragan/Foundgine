using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Planning.Mutation;

namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Canonical provider-neutral execution representation for mutation work.
///
/// Mutation semantics are resolved before this boundary. This representation
/// contains the concrete provider-neutral work required to execute a mutation
/// batch, including dependency edges, but contains no SQL or provider-specific
/// plan types.
/// </summary>
public sealed record ExecutionMutationIR(
    IReadOnlyList<MutationOperation> Operations,
    IReadOnlyList<MutationDependency> Dependencies)
{
    /// <summary>Security invariants required to execute this exact mutation IR.</summary>
    public IReadOnlyList<string> RequiredSecurityInvariants { get; init; } = [];

    public static ExecutionMutationIR From(MutationBatchPlan plan,
        IReadOnlyList<string>? requiredSecurityInvariants = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Operations.Count == 0)
            throw new InvalidOperationException(
                "An execution mutation IR must contain at least one operation.");

        ValidateDependencies(plan.Operations.Count, plan.Dependencies);

        return new ExecutionMutationIR(
            plan.Operations.ToArray(),
            plan.Dependencies.ToArray())
        {
            RequiredSecurityInvariants = requiredSecurityInvariants is null
                ? []
                : requiredSecurityInvariants.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray()
        };
    }

    /// <summary>
    /// Materializes the canonical provider-neutral mutation batch consumed by
    /// existing mutation compilers. No correlation-specific representation is
    /// introduced here.
    /// </summary>
    public MutationBatchPlan ToMutationBatchPlan() =>
        new(Operations, Dependencies);

    /// <summary>
    /// Derives dependency edges from field-level value references. This is a
    /// validation/consistency operation; the Dependencies collection remains
    /// the canonical execution graph input.
    /// </summary>
    public IReadOnlyList<MutationDependency> DeriveDependencies()
    {
        var result = new List<MutationDependency>();

        for (var targetIndex = 0; targetIndex < Operations.Count; targetIndex++)
        {
            foreach (var field in Operations[targetIndex].Fields)
            {
                var source = field.Source;
                if (source is null)
                    continue;

                if (source.SourceOperationIndex < 0 ||
                    source.SourceOperationIndex >= Operations.Count)
                {
                    throw new InvalidOperationException(
                        $"Mutation value reference points to invalid source operation " +
                        $"{source.SourceOperationIndex}.");
                }

                if (source.SourceOperationIndex >= targetIndex)
                {
                    throw new InvalidOperationException(
                        $"Mutation value reference must point from an earlier operation: " +
                        $"{source.SourceOperationIndex} -> {targetIndex}.");
                }

                result.Add(new MutationDependency(
                    source.SourceOperationIndex,
                    targetIndex,
                    source.SourceField,
                    field.Column));
            }
        }

        return result;
    }

    /// <summary>
    /// Ensures canonical dependency metadata agrees with dependency edges
    /// derivable from field-level value references.
    /// </summary>
    public void ValidateDerivedDependencies()
    {
        var expected = DeriveDependencies()
            .OrderBy(x => x.SourceOperationIndex)
            .ThenBy(x => x.TargetOperationIndex)
            .ThenBy(x => x.SourceField.Value)
            .ThenBy(x => x.TargetColumn.Value)
            .ToArray();

        var actual = Dependencies
            .OrderBy(x => x.SourceOperationIndex)
            .ThenBy(x => x.TargetOperationIndex)
            .ThenBy(x => x.SourceField.Value)
            .ThenBy(x => x.TargetColumn.Value)
            .ToArray();

        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                "Mutation dependency metadata disagrees with field correlation references.");
        }
    }

    private static void ValidateDependencies(
        int operationCount,
        IReadOnlyList<MutationDependency> dependencies)
    {
        foreach (var dependency in dependencies)
        {
            if (dependency.SourceOperationIndex < 0 ||
                dependency.SourceOperationIndex >= operationCount ||
                dependency.TargetOperationIndex < 0 ||
                dependency.TargetOperationIndex >= operationCount)
            {
                throw new InvalidOperationException(
                    $"Mutation dependency indexes are outside the execution IR: " +
                    $"{dependency.SourceOperationIndex} -> {dependency.TargetOperationIndex}.");
            }

            if (dependency.SourceOperationIndex >= dependency.TargetOperationIndex)
            {
                throw new InvalidOperationException(
                    $"Mutation dependency must point from an earlier operation: " +
                    $"{dependency.SourceOperationIndex} -> {dependency.TargetOperationIndex}.");
            }
        }
    }
}

/// <summary>
/// Explicit lowering from the provider-neutral mutation planning artifact to
/// the canonical execution representation.
/// </summary>
public static class ExecutionMutationIRCompiler
{
    public static ExecutionMutationIR Compile(MutationBatchPlan plan) =>
        ExecutionMutationIR.From(plan);
}