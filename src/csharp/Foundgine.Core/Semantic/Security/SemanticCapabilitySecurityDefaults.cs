using Foundgine.Core.Semantic.Capabilities;

namespace Foundgine.Core.Semantic.Security;

/// <summary>Derives the minimum security contract for generic semantic capabilities.</summary>
public static class SemanticCapabilitySecurityDefaults
{
    public static IReadOnlyList<string> For(SemanticCapability capability)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal)
        {
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.ParameterizedValues
        };

        if (capability.Fields.Count > 0)
            ids.Add(SecurityInvariantIds.FieldVisibility);
        if (capability.Relationships.Count > 0)
            ids.Add(SecurityInvariantIds.RelationshipVisibility);
        if (capability.HasSideEffects ||
            capability.Access.Access == Foundgine.Core.Abstractions.AuthorizationAccess.Conditional)
            ids.Add(SecurityInvariantIds.RuntimeAuthorization);

        return ids.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
}