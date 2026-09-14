namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Enforces the complete trust boundary for a warrant presented to execution.
/// Root warrants are anchored to the configured issuer. Delegated warrants must
/// carry their exact root-to-leaf chain; every signature is re-verified, every
/// delegation edge is structurally validated, and each delegating issuer must be
/// explicitly trusted for delegation.
/// </summary>
public static class SecurityWarrantExecutionTrust
{
    public static void Verify(
        SecurityWarrant leaf,
        ISecurityWarrantKeyResolver keys,
        string expectedIssuer,
        string? expectedAudience,
        DateTimeOffset now,
        IReadOnlyList<SecurityWarrant>? suppliedChain,
        ISecurityWarrantDelegationTrustResolver? delegationTrust,
        string? tenant)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        ArgumentNullException.ThrowIfNull(keys);
        if (string.IsNullOrWhiteSpace(expectedIssuer))
            throw new InvalidOperationException("Warrant execution requires an explicit trusted issuer.");

        var chain = suppliedChain;
        if (leaf.ParentId is null && leaf.ParentDigest is null && leaf.DelegationPath.Count == 0)
        {
            if (chain is not null && (chain.Count != 1 || !StringComparer.Ordinal.Equals(chain[0].Digest, leaf.Digest)))
                throw new InvalidOperationException(
                    "A root warrant may only be accompanied by itself as the delegation chain.");

            SecurityWarrantVerifier.Verify(leaf, keys, now, expectedIssuer, expectedAudience);
            return;
        }

        if (chain is null || chain.Count < 2)
            throw new InvalidOperationException(
                "Delegated warrant execution requires the complete root-to-leaf delegation chain.");
        if (!StringComparer.Ordinal.Equals(chain[^1].Digest, leaf.Digest))
            throw new InvalidOperationException(
                "The supplied delegation chain does not terminate at the executing warrant.");
        if (delegationTrust is null)
            throw new InvalidOperationException(
                "Delegated warrant execution requires an explicit delegation trust resolver.");

        SecurityWarrantDelegationChainValidator.Validate(chain, now);
        SecurityWarrantVerifier.Verify(chain[0], keys, now, expectedIssuer, expectedAudience);

        for (var i = 1; i < chain.Count; i++)
        {
            var parent = chain[i - 1];
            var child = chain[i];
            SecurityWarrantVerifier.Verify(child, keys, now, parent.Subject, expectedAudience);
            SecurityWarrantDelegationTrust.VerifyIssuer(parent, child, delegationTrust, now, tenant);
            if (!child.Constraints.IsAtMostAsPowerfulAs(parent.Constraints))
                throw new InvalidOperationException("Delegation cannot increase warrant authority.");
            if (!child.Grants.All(grant => parent.Grants.Any(parentGrant =>
                    StringComparer.Ordinal.Equals(grant.Capability, parentGrant.Capability) &&
                    StringComparer.Ordinal.Equals(grant.Operation, parentGrant.Operation) &&
                    grant.ResourceScopes.All(scope =>
                        parentGrant.ResourceScopes.Count == 0 ||
                        parentGrant.ResourceScopes.Contains(scope, StringComparer.Ordinal)))))
                throw new InvalidOperationException("Delegation cannot grant capabilities outside the parent warrant.");
        }
    }
}