namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Proves that a plan rewrite did not detach the executable plan from the
/// authorization decision and semantic contract that produced it.
/// </summary>
public sealed record SemanticPlanAuthorizationBindingProof(
    SemanticPlanAuthorizationBinding? Before,
    SemanticPlanAuthorizationBinding? After)
{
    public bool IsSatisfied => AreEqual(Before, After);

    public static SemanticPlanAuthorizationBindingProof Create(
        SemanticPlan before,
        SemanticPlan after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var proof = new SemanticPlanAuthorizationBindingProof(
            before.AuthorizationBinding,
            after.AuthorizationBinding);

        if (!proof.IsSatisfied)
        {
            throw new InvalidOperationException(
                "Plan rewrite rejected: authorization binding was added, removed, or changed by the rewrite.");
        }

        return proof;
    }

    private static bool AreEqual(
        SemanticPlanAuthorizationBinding? before,
        SemanticPlanAuthorizationBinding? after)
    {
        if (ReferenceEquals(before, after))
            return true;
        if (before is null || after is null)
            return false;

        return string.Equals(before.ContractFingerprint, after.ContractFingerprint, StringComparison.Ordinal) &&
               string.Equals(before.AuthorizationFingerprint, after.AuthorizationFingerprint, StringComparison.Ordinal);
    }
}
