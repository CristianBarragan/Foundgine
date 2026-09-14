using Foundgine.Core.Semantic.Capabilities;

namespace Foundgine.Core.Semantic.Security;

/// <summary>
/// Validates the machine-readable security contract before a capability is
/// allowed to cross into provider planning. This is a structural proof gate,
/// not an authorization decision.
/// </summary>
public static class SecurityInvariantContractValidator
{
    public static IReadOnlyList<string> Validate(SemanticCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        var errors = new List<string>();
        var invariants = capability.EffectiveSecurityInvariants;

        // Validate explicitly supplied identifiers independently of the derived
        // default set. This keeps unknown identifiers observable and fail-closed
        // even when the capability also carries field/relationship metadata.
        foreach (var id in capability.RequiredSecurityInvariants)
        {
            if (!SecurityInvariantRegistry.Contains(id))
                errors.Add($"Capability '{capability.Id}' references unknown security invariant '{id}'.");
        }

        if (capability.HasSideEffects && !invariants.Contains(SecurityInvariantIds.RuntimeAuthorization))
            errors.Add(
                $"Mutating capability '{capability.Id}' must require security invariant '{SecurityInvariantIds.RuntimeAuthorization}'.");

        if (capability.HasSideEffects && !invariants.Contains(SecurityInvariantIds.AuthorizationRequired))
            errors.Add(
                $"Mutating capability '{capability.Id}' must require security invariant '{SecurityInvariantIds.AuthorizationRequired}'.");

        if (capability.Fields.Count > 0 && !invariants.Contains(SecurityInvariantIds.FieldVisibility))
            errors.Add(
                $"Capability '{capability.Id}' exposes fields without security invariant '{SecurityInvariantIds.FieldVisibility}'.");

        if (capability.Relationships.Count > 0 && !invariants.Contains(SecurityInvariantIds.RelationshipVisibility))
            errors.Add(
                $"Capability '{capability.Id}' exposes relationships without a relationship-visibility invariant.");

        return errors;
    }

    public static void EnsureValid(SemanticCapability capability)
    {
        var errors = Validate(capability);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
    }

    public static void EnsureContractValid(SemanticCapabilityContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var errors = contract.Capabilities.SelectMany(Validate).ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
}