using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Authorization;

/// <summary>
/// Names the composition rule that <see cref="SemanticAuthorizer"/> and
/// <see cref="SemanticAuthorizationCapabilityDiscovery"/> already apply
/// inline: authorizing across entities, fields, or relationships intersects
/// their decisions and ANDs their predicates. It never unions them.
/// For example, resolving <c>Customer.Read</c> reachable through
/// <c>Order.Read</c> into <c>Invoice.Read</c> must compose as
/// <c>Customer.TenantId == Context.TenantId AND Order.TenantId ==
/// Context.TenantId AND Invoice.TenantId == Context.TenantId</c>, not as
/// "any one of these being allowed is enough."
/// </summary>
public static class SemanticAuthorizationCapabilityComposition
{
    /// <summary>
    /// Composes zero or more authorization decisions into a single decision.
    /// The result is allowed only if every input is allowed, and its
    /// predicate is the conjunction of every input predicate. This is strict
    /// intersection: composing is monotonic and can only narrow authority,
    /// never widen it.
    /// </summary>
    public static AuthorizationDecision Compose(params AuthorizationDecision[] decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        var result = AuthorizationDecision.Allowed;
        foreach (var decision in decisions)
            result = AuthorizationDecision.Combine(result, decision);
        return result;
    }

    /// <summary>Overload for composing an already-materialized sequence.</summary>
    public static AuthorizationDecision Compose(IEnumerable<AuthorizationDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        return Compose(decisions.ToArray());
    }
}
