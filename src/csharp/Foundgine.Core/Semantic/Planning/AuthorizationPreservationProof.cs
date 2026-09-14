namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Records that a rewrite did not weaken the source plan's security contract: every security
/// invariant required by the plan before rewriting is still required after. A rewrite is free
/// to add invariants (e.g. because it introduces a new authorization-sensitive shape) but must
/// never silently drop one — dropping an invariant is exactly how a "purely semantic"
/// optimization could quietly regress a security guarantee.
/// </summary>
public sealed record AuthorizationPreservationProof(bool IsSatisfied, IReadOnlyList<string> Violations)
{
    public static AuthorizationPreservationProof Create(SemanticPlan before, SemanticPlan after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var afterInvariants = new HashSet<string>(after.EffectiveSecurityInvariants, StringComparer.Ordinal);

        var dropped = before.EffectiveSecurityInvariants
            .Where(id => !afterInvariants.Contains(id))
            .ToArray();

        if (dropped.Length == 0)
            return new AuthorizationPreservationProof(true, []);

        return new AuthorizationPreservationProof(
            false,
            dropped
                .Select(id =>
                    $"security invariant '{id}' required by the source plan is not preserved by the rewritten plan.")
                .ToArray());
    }
}
