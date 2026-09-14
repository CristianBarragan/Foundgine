using Foundgine.Core.Semantic.Security;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Derives the minimum plan-level security contract from the authorized plan
/// shape. Capability-specific requirements can be supplied explicitly and are
/// preserved rather than replaced.
/// </summary>
public static class SecurityInvariantPlanRequirements
{
    public static SemanticPlan Attach(SemanticPlan plan) => Attach(plan, null);

    /// <summary>
    /// Attaches the plan obligations plus the invariants declared by the
    /// capability/security contract. The supplied capability invariants are
    /// additive: plan shape may require more guarantees, never fewer.
    /// </summary>
    public static SemanticPlan Attach(
        SemanticPlan plan,
        IEnumerable<string>? capabilityInvariants)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var ids = new HashSet<string>(
            plan.RequiredSecurityInvariants ?? [],
            StringComparer.Ordinal);

        if (capabilityInvariants is not null)
        {
            foreach (var id in capabilityInvariants)
            {
                if (!SecurityInvariantRegistry.Contains(id))
                    throw new InvalidOperationException(
                        $"Unknown capability security invariant '{id}'.");
                ids.Add(id);
            }
        }

        Collect(plan.Root, ids);

        if (ids.Count == 0)
            throw new InvalidOperationException(
                "A semantic plan cannot become executable without a security contract.");

        return plan with
        {
            RequiredSecurityInvariants = ids
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void Collect(SemanticPlanNode node, ISet<string> ids)
    {
        ids.Add(SecurityInvariantIds.AuthorizationRequired);
        ids.Add(SecurityInvariantIds.ParameterizedValues);
        ids.Add(SecurityInvariantIds.PlanCacheContextIsolation);

        if (node.Fields.Count > 0)
            ids.Add(SecurityInvariantIds.FieldVisibility);

        if (node.Children.Count > 0)
            ids.Add(SecurityInvariantIds.RelationshipVisibility);

        if (node.Authorization is not null)
        {
            ids.Add(SecurityInvariantIds.RuntimeAuthorization);
        }

        foreach (var child in node.Children)
            Collect(child, ids);
    }
}
