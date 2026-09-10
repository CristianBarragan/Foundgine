using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.Security.Warrants;

namespace Foundgine.Core.Semantic.Security;

/// <summary>
/// Validates composition of multiple capabilities as a single security contract.
/// Composition never unions authority: every component must be independently
/// authorized and the resulting authority is bounded by the intersection of
/// the active warrant constraints.
/// </summary>
public static class SecurityCapabilityComposition
{
    public static SecurityCapabilityCompositionResult Validate(
        IEnumerable<SemanticCapability> capabilities,
        SecurityWarrant warrant,
        string subject,
        string audience,
        string? tenant,
        string? resourceScope,
        IEnumerable<string>? requestedFields = null,
        long? requestedResults = null,
        decimal? requestedAmount = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(warrant);

        var components = capabilities
            .DistinctBy(x => x.Id, StringComparer.Ordinal)
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        if (components.Length == 0)
            return SecurityCapabilityCompositionResult.Rejected(
                "A security composition must contain at least one capability.");

        foreach (var capability in components)
        {
            SecurityInvariantContractValidator.EnsureValid(capability);

            // The composed resourceScope describes the request as a whole, not
            // any single component: components may legitimately have different,
            // narrower grant scopes (e.g. "customer/*" vs "order/*"). Requiring
            // each component's own grant to match the composed-level scope would
            // reject valid compositions across independently-scoped capabilities.
            // The composed scope is still enforced once, holistically, against
            // the warrant's Constraints below.
            if (!SecurityWarrantAuthorization.Allows(
                    warrant,
                    subject,
                    audience,
                    capability.Id,
                    capability.Operation,
                    tenant,
                    resourceScope,
                    requestedResults,
                    requestedAmount,
                    requireResourceScopeMatch: false))
            {
                return SecurityCapabilityCompositionResult.Rejected(
                    $"Capability composition is not authorized because '{capability.Id}' is not independently authorized.");
            }
        }

        var fields = (requestedFields ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (warrant.Constraints.AllowedFields.Count > 0 &&
            fields.Any(field => !warrant.Constraints.AllowedFields.Contains(field, StringComparer.Ordinal)))
        {
            return SecurityCapabilityCompositionResult.Rejected(
                "Capability composition requests a field outside the warrant's allowed field set.");
        }

        // A composed operation may only use one caller/tenant/resource authority.
        // There is deliberately no union operation here: incompatible components
        // fail closed rather than producing a broader synthetic authority.
        if (tenant is not null && warrant.Constraints.AllowedTenants.Count > 0 &&
            !warrant.Constraints.AllowedTenants.Contains(tenant, StringComparer.Ordinal))
            return SecurityCapabilityCompositionResult.Rejected(
                "Capability composition crosses the warrant tenant boundary.");

        if (resourceScope is not null && warrant.Constraints.ResourceScopes.Count > 0 &&
            !warrant.Constraints.ResourceScopes.Contains(resourceScope, StringComparer.Ordinal))
            return SecurityCapabilityCompositionResult.Rejected(
                "Capability composition crosses the warrant resource boundary.");

        var invariants = components
            .SelectMany(x => x.EffectiveSecurityInvariants)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        foreach (var invariant in invariants)
            if (!SecurityInvariantRegistry.Contains(invariant))
                return SecurityCapabilityCompositionResult.Rejected(
                    $"Capability composition contains unknown security invariant '{invariant}'.");

        return SecurityCapabilityCompositionResult.Accepted(components, invariants);
    }
}

public sealed record SecurityCapabilityCompositionResult(
    bool IsSatisfied,
    IReadOnlyList<SemanticCapability> Components,
    IReadOnlyList<string> EffectiveSecurityInvariants,
    string? FailureReason)
{
    public static SecurityCapabilityCompositionResult Accepted(
        IReadOnlyList<SemanticCapability> components,
        IReadOnlyList<string> invariants) =>
        new(true, components, invariants, null);

    public static SecurityCapabilityCompositionResult Rejected(string reason) =>
        new(false, [], [], reason);
}