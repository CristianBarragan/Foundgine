using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Machine-readable evidence that every security obligation declared by a
/// rewrite rule was evaluated against the source and rewritten plans.
/// An obligation is either present in the source contract and must remain
/// present, or it is not required by the source contract. The latter is kept
/// explicit so the proof never pretends that an unenforced obligation was
/// actually exercised.
/// </summary>
public sealed record SecurityObligationProof(
    IReadOnlyList<string> Obligations,
    IReadOnlyList<string> Preserved,
    IReadOnlyList<string> NotRequired,
    IReadOnlyList<string> Violations)
{
    public bool IsSatisfied => Violations.Count == 0 &&
                               Obligations.OrderBy(x => x, StringComparer.Ordinal)
                                   .SequenceEqual(
                                       Preserved.Concat(NotRequired).OrderBy(x => x, StringComparer.Ordinal),
                                       StringComparer.Ordinal);

    public static SecurityObligationProof Create(
        IPlanRewriteRule rule,
        SemanticPlan before,
        SemanticPlan after)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var obligations = rule.SecurityObligations
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var violations = new List<string>();
        foreach (var obligation in obligations)
        {
            if (!SecurityInvariantRegistry.Contains(obligation))
            {
                violations.Add(
                    $"Rewrite rule '{rule.Name}' declares unknown security obligation '{obligation}'.");
            }
        }

        var beforeSet = before.EffectiveSecurityInvariants.ToHashSet(StringComparer.Ordinal);
        var afterSet = after.EffectiveSecurityInvariants.ToHashSet(StringComparer.Ordinal);
        var preserved = new List<string>();
        var notRequired = new List<string>();

        foreach (var obligation in obligations)
        {
            if (!SecurityInvariantRegistry.Contains(obligation))
                continue;

            if (!beforeSet.Contains(obligation))
            {
                notRequired.Add(obligation);
                continue;
            }

            if (!afterSet.Contains(obligation))
            {
                violations.Add(
                    $"Rewrite rule '{rule.Name}' dropped declared security obligation '{obligation}'.");
                continue;
            }

            preserved.Add(obligation);
        }

        var proof = new SecurityObligationProof(obligations, preserved, notRequired, violations);
        if (!proof.IsSatisfied)
            throw new InvalidOperationException(string.Join(" ", violations));

        return proof;
    }
}