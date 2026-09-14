namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Records that a rewrite preserved the provider-neutral semantic meaning of
/// a plan. This is an equivalence check over Foundgine's canonical semantic
/// representation; it is not a proof that a provider implementation is bug-free.
/// </summary>
public sealed record SemanticEquivalenceProof(
    string BeforeFingerprint,
    string AfterFingerprint)
{
    public bool IsSatisfied => string.Equals(BeforeFingerprint, AfterFingerprint, StringComparison.Ordinal);

    public static SemanticEquivalenceProof Create(SemanticPlan before, SemanticPlan after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var proof = new SemanticEquivalenceProof(
            SemanticEquivalenceFingerprint.Create(before),
            SemanticEquivalenceFingerprint.Create(after));

        if (!proof.IsSatisfied)
            throw new InvalidOperationException(
                "Semantic rewrite rejected because the rewritten plan does not preserve the canonical semantic meaning of the source plan.");

        return proof;
    }
}
