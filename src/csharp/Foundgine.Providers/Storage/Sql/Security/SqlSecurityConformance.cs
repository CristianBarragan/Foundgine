using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Security;

namespace Foundgine.Providers.Storage.Sql.Security;

/// <summary>
/// Structural conformance checks for a compiled SQL plan. These checks do not
/// prove arbitrary provider correctness; they verify that the SQL plan exposes
/// the concrete evidence required by the Foundgine security contract.
/// </summary>
public sealed record SqlSecurityConformanceResult(
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Satisfied,
    IReadOnlyList<string> Violations)
{
    public bool IsSatisfied => Violations.Count == 0;

    public void EnsureSatisfied()
    {
        if (!IsSatisfied)
            throw new InvalidOperationException(
                "SQL security conformance failed: " + string.Join("; ", Violations));
    }
}

public static class SqlSecurityConformance
{
    public static SqlSecurityConformanceResult Verify(
        ExecutionIR ir,
        SqlPlan plan)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(plan);

        var required = ir.RequiredSecurityInvariants
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var satisfied = new List<string>();
        var violations = new List<string>();

        foreach (var invariant in required)
        {
            if (!SecurityInvariantRegistry.Contains(invariant))
            {
                violations.Add($"Unknown security invariant '{invariant}'.");
                continue;
            }

            switch (invariant)
            {
                case SecurityInvariantIds.AuthorizationRequired:
                case SecurityInvariantIds.RuntimeAuthorization:
                    VerifyAuthorization(invariant, plan, satisfied, violations);
                    break;

                case SecurityInvariantIds.ParameterizedValues:
                    VerifyParameterizedValues(plan, satisfied, violations);
                    break;

                case SecurityInvariantIds.FieldVisibility:
                    VerifyFieldProjection(plan, satisfied, violations);
                    break;

                case SecurityInvariantIds.RelationshipVisibility:
                    VerifyRelationshipShape(ir, plan, satisfied, violations);
                    break;

                case SecurityInvariantIds.PlanCacheContextIsolation:
                    VerifyContextIsolation(plan, satisfied, violations);
                    break;

                default:
                    // Mutation/evidence invariants require provider-specific
                    // execution contracts and are intentionally not inferred
                    // from ordinary query SQL.
                    violations.Add(
                        $"Invariant '{invariant}' requires a provider-specific conformance check and cannot be inferred from SqlPlan alone.");
                    break;
            }
        }

        return new SqlSecurityConformanceResult(required, satisfied, violations);
    }

    public static void EnsureSatisfied(ExecutionIR ir, SqlPlan plan) =>
        Verify(ir, plan).EnsureSatisfied();

    private static void VerifyAuthorization(
        string invariant,
        SqlPlan plan,
        ICollection<string> satisfied,
        ICollection<string> violations)
    {
        if (plan.Authorization is null || plan.Authorization.Count == 0)
        {
            violations.Add($"{invariant} requires at least one compiled authorization predicate.");
            return;
        }

        if (plan.Authorization.Count != plan.Authorization.Select(x => x.NodeId).Distinct().Count())
        {
            violations.Add($"{invariant} produced duplicate authorization predicates for the same execution node.");
            return;
        }

        if (invariant == SecurityInvariantIds.RuntimeAuthorization)
        {
            var authorizationParameters = plan.EffectiveParameters
                .Where(x => x.Name.StartsWith("auth", StringComparison.Ordinal))
                .ToArray();

            if (authorizationParameters.Length == 0 ||
                authorizationParameters.Any(x => string.IsNullOrWhiteSpace(x.ContextPath) && x.Value is null))
            {
                violations.Add(
                    "authorization.runtime requires authorization context values to remain bound parameters.");
                return;
            }
        }

        satisfied.Add(invariant);
    }

    private static void VerifyParameterizedValues(
        SqlPlan plan,
        ICollection<string> satisfied,
        ICollection<string> violations)
    {
        if (plan.EffectiveParameters is null)
        {
            violations.Add("execution.parameterized-values requires a parameter binding collection.");
            return;
        }

        // The compiler emits all semantic values through SqlParameterBinding.
        // A parameter may legitimately have a null ContextPath when it is a
        // literal or pagination value; the important invariant is that it has
        // a binding rather than being interpolated into SQL.
        if (plan.EffectiveParameters.Any(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            violations.Add("execution.parameterized-values contains an unnamed parameter binding.");
            return;
        }

        satisfied.Add(SecurityInvariantIds.ParameterizedValues);
    }

    private static void VerifyFieldProjection(
        SqlPlan plan,
        ICollection<string> satisfied,
        ICollection<string> violations)
    {
        if (plan.Columns.Count == 0)
        {
            violations.Add("visibility.field requires an explicit SQL column projection.");
            return;
        }

        if (plan.Columns.Any(x => string.IsNullOrWhiteSpace(x.ColumnName)))
        {
            violations.Add("visibility.field contains an SQL projection without a mapped storage column.");
            return;
        }

        satisfied.Add(SecurityInvariantIds.FieldVisibility);
    }

    private static void VerifyRelationshipShape(
        ExecutionIR ir,
        SqlPlan plan,
        ICollection<string> satisfied,
        ICollection<string> violations)
    {
        var executionNodes = Flatten(ir.Root).ToArray();
        var projectedNodes = plan.Columns.Select(x => x.NodeId).Distinct().ToHashSet();

        if (executionNodes.Any(x => x.Children.Count > 0) && projectedNodes.Count == 0)
        {
            violations.Add("visibility.relationship requires an explicit projected execution shape.");
            return;
        }

        satisfied.Add(SecurityInvariantIds.RelationshipVisibility);
    }

    private static void VerifyContextIsolation(
        SqlPlan plan,
        ICollection<string> satisfied,
        ICollection<string> violations)
    {
        // Request-specific authorization values must be represented as
        // bindings, never embedded into CommandText. This check is structural:
        // it cannot prove the absence of every possible provider bug.
        foreach (var binding in plan.EffectiveParameters.Where(x => x.ContextPath is not null))
        {
            if (plan.CommandText.Contains(binding.Value?.ToString() ?? "\u0000", StringComparison.Ordinal))
            {
                violations.Add("planning.cache-context-isolation found a context value embedded in CommandText.");
                return;
            }
        }

        satisfied.Add(SecurityInvariantIds.PlanCacheContextIsolation);
    }

    private static IEnumerable<ExecutionIRNode> Flatten(ExecutionIRNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Flatten(child))
            yield return nested;
    }
}