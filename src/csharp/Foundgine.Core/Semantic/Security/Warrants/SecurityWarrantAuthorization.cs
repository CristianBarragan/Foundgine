namespace Foundgine.Core.Semantic.Security.Warrants;

/// <summary>
/// Runtime authorization over a verified warrant. It deliberately requires the
/// current subject, audience, tenant and resource rather than trusting values
/// supplied by an agent or transport.
/// </summary>
public static class SecurityWarrantAuthorization
{
    public static bool Allows(
        SecurityWarrant warrant,
        string subject,
        string audience,
        string capability,
        string operation,
        string? tenant,
        string? resourceScope,
        long? requestedResults = null,
        decimal? requestedAmount = null,
        bool requireResourceScopeMatch = true)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        if (!StringComparer.Ordinal.Equals(warrant.Subject, subject) ||
            !StringComparer.Ordinal.Equals(warrant.Audience, audience) ||
            !warrant.IsTimeValid(DateTimeOffset.UtcNow))
            return false;

        // requireResourceScopeMatch is false only for per-component checks made
        // while validating a capability composition (see
        // SecurityCapabilityComposition.Validate). A composed operation's single
        // resourceScope value describes the composed request as a whole and has
        // no reason to equal any one component's own (necessarily narrower and
        // possibly differently-typed) grant scope; that per-component grant-scope
        // match would otherwise reject compositions over independently-scoped
        // capabilities even when every component is genuinely granted. The
        // composed resourceScope is still checked once, holistically, against
        // the warrant's Constraints by the caller.
        var grant = warrant.Grants.Any(g =>
            StringComparer.Ordinal.Equals(g.Capability, capability) &&
            StringComparer.Ordinal.Equals(g.Operation, operation) &&
            (!requireResourceScopeMatch ||
             g.ResourceScopes.Count == 0 ||
             (resourceScope is not null && g.ResourceScopes.Contains(resourceScope, StringComparer.Ordinal))));
        if (!grant)
            return false;

        var c = warrant.Constraints;
        // Fail closed: if the warrant restricts tenant/resource, a missing runtime
        // value is a missing constraint check, not an implicit pass. Only an
        // unrestricted warrant (empty constraint set) tolerates a null runtime value.
        if (c.AllowedTenants.Count > 0 &&
            (tenant is null || !c.AllowedTenants.Contains(tenant, StringComparer.Ordinal))) return false;
        if (c.AllowedOperations.Count > 0 && !c.AllowedOperations.Contains(operation, StringComparer.Ordinal))
            return false;
        if (requireResourceScopeMatch &&
            c.ResourceScopes.Count > 0 &&
            (resourceScope is null || !c.ResourceScopes.Contains(resourceScope, StringComparer.Ordinal))) return false;
        if (requestedResults is not null && c.MaxResults is not null && requestedResults > c.MaxResults) return false;
        if (requestedAmount is not null && c.MaxAmount is not null && requestedAmount > c.MaxAmount) return false;
        return true;
    }
}

/// <summary>Mechanically enforces non-escalating warrant delegation.</summary>
public static class SecurityWarrantAttenuator
{
    public const int MaxDelegationDepth = 32;

    public static SecurityWarrant Attenuate(
        SecurityWarrant parent,
        SecurityWarrant child,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        if (!parent.IsTimeValid(now))
            throw new InvalidOperationException("Cannot attenuate an expired or not-yet-valid parent warrant.");
        if (child.ParentId is null || !StringComparer.Ordinal.Equals(child.ParentId, parent.Id))
            throw new InvalidOperationException("Child warrant must identify its parent warrant.");
        if (!StringComparer.Ordinal.Equals(child.ParentDigest, parent.Digest))
            throw new InvalidOperationException("Child warrant must bind cryptographically to its parent digest.");
        if (!StringComparer.Ordinal.Equals(child.Issuer, parent.Subject))
            throw new InvalidOperationException("Delegated issuer must be the parent subject.");
        if (!StringComparer.Ordinal.Equals(child.Audience, parent.Audience))
            throw new InvalidOperationException("Delegation cannot broaden the audience.");
        if (child.Subject.Length == 0)
            throw new InvalidOperationException("Delegated subject is required.");
        if (child.IssuedAt < parent.IssuedAt)
            throw new InvalidOperationException("Child warrant cannot predate parent issuance.");
        if (child.ExpiresAt > parent.ExpiresAt)
            throw new InvalidOperationException("Child warrant cannot extend parent expiry.");
        if (child.DelegationDepth != parent.DelegationDepth + 1)
            throw new InvalidOperationException("Delegation depth must increase exactly one level.");
        if (child.DelegationDepth > MaxDelegationDepth)
            throw new InvalidOperationException("Maximum delegation depth exceeded.");
        if (!SequenceEqual(child.DelegationPath.Take(parent.DelegationDepth), parent.DelegationPath) ||
            child.DelegationPath.LastOrDefault() != parent.Digest)
            throw new InvalidOperationException("Child delegation path does not match the parent chain.");
        if (child.DelegationPath.Distinct(StringComparer.Ordinal).Count() != child.DelegationPath.Count)
            throw new InvalidOperationException("Delegation cycle detected.");
        if (!child.Constraints.IsAtMostAsPowerfulAs(parent.Constraints))
            throw new InvalidOperationException("Child warrant constraints broaden parent authority.");

        foreach (var grant in child.Grants)
        {
            var parentGrant = parent.Grants.FirstOrDefault(g =>
                StringComparer.Ordinal.Equals(g.Capability, grant.Capability) &&
                StringComparer.Ordinal.Equals(g.Operation, grant.Operation));
            if (parentGrant is null)
                throw new InvalidOperationException($"Child warrant adds capability '{grant.Capability}'.");
            if (parentGrant.ResourceScopes.Count > 0 &&
                grant.ResourceScopes.Any(x => !parentGrant.ResourceScopes.Contains(x, StringComparer.Ordinal)))
                throw new InvalidOperationException($"Child warrant broadens resource scope for '{grant.Capability}'.");
        }

        return child;

        static bool SequenceEqual(IEnumerable<string> left, IEnumerable<string> right) =>
            left.SequenceEqual(right, StringComparer.Ordinal);
    }
}