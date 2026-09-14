using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Records that a semantic rewrite preserved the security contract of the
/// input plan. This is a rewrite proof, not a claim that the provider is
/// correct or that authorization has been granted.
/// </summary>
public sealed record SecurityPreservationProof(
    IReadOnlyList<string> Before,
    IReadOnlyList<string> After,
    IReadOnlyList<string> Missing,
    string BeforeFingerprint,
    string AfterFingerprint)
{
    public bool IsSatisfied => Missing.Count == 0 &&
                               Before.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                                   After.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

    public static SecurityPreservationProof Create(SemanticPlan before, SemanticPlan after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        foreach (var id in before.EffectiveSecurityInvariants)
            if (!SecurityInvariantRegistry.Contains(id))
                throw new InvalidOperationException($"Unknown security invariant '{id}' in source plan.");

        foreach (var id in after.EffectiveSecurityInvariants)
            if (!SecurityInvariantRegistry.Contains(id))
                throw new InvalidOperationException($"Unknown security invariant '{id}' in rewritten plan.");

        var afterSet = after.EffectiveSecurityInvariants.ToHashSet(StringComparer.Ordinal);
        var missing = before.EffectiveSecurityInvariants
            .Where(id => !afterSet.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var proof = new SecurityPreservationProof(
            before.EffectiveSecurityInvariants.ToArray(),
            after.EffectiveSecurityInvariants.ToArray(),
            missing,
            SemanticPlanFingerprint.Create(before),
            SemanticPlanFingerprint.Create(after));

        if (!proof.IsSatisfied)
            throw new InvalidOperationException(
                $"Security-preserving rewrite rejected. Missing invariants: {string.Join(", ", missing)}.");

        return proof;
    }
}