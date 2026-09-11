using System.Text;
using Foundgine.Core.Abstractions;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Canonicalizes authorization boolean expressions without changing their
/// provider-neutral meaning. This is the first concrete implementation of the
/// rewrite-rule contract and is intentionally limited to policy-expression
/// normalization.
/// </summary>
public sealed class AuthorizationCanonicalizationRule : IPlanRewriteRule
{
    public string Name => "authorization.canonicalization";

    public IReadOnlyList<string> Preconditions =>
        ["plan contains an authorization predicate", "predicate uses supported boolean structure"];

    public IReadOnlyList<string> SecurityObligations =>
        ["authorization.required", "authorization.runtime"];

    public double CostImpact => 0d;

    public double BenefitEstimate => 1d;

    public bool IsIdempotent => true;

    public int Priority => 0;

    public bool CanApply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ContainsAuthorization(plan.Root);
    }

    public SemanticPlan Apply(SemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!CanApply(plan))
            return plan;

        var changed = false;
        var root = RewriteNode(plan.Root, ref changed);
        return changed ? new SemanticPlan(root, plan.RequiredSecurityInvariants, plan.AuthorizationBinding) : plan;
    }

    private static SemanticPlanNode RewriteNode(SemanticPlanNode node, ref bool changed)
    {
        var authorization = node.Authorization;
        if (authorization is not null)
        {
            var normalized = Normalize(authorization, ref changed);
            authorization = normalized;
        }

        var children = new SemanticPlanNode[node.Children.Count];
        var childrenChanged = false;
        for (var i = 0; i < node.Children.Count; i++)
        {
            children[i] = RewriteNode(node.Children[i], ref changed);
            childrenChanged |= !ReferenceEquals(children[i], node.Children[i]);
        }

        var nodeChanged = !ReferenceEquals(authorization, node.Authorization) || childrenChanged;
        if (nodeChanged)
            changed = true;

        return nodeChanged
            ? node with { Authorization = authorization, Children = children }
            : node;
    }

    private static bool ContainsAuthorization(SemanticPlanNode node) =>
        node.Authorization is not null || node.Children.Any(ContainsAuthorization);

    private static AuthorizationPredicate Normalize(AuthorizationPredicate predicate, ref bool changed)
    {
        // Normalize using a local change flag. The previous implementation
        // rebuilt an equivalent predicate object on every pass, which made an
        // idempotent canonicalization rule appear to rewrite the plan forever.
        var localChanged = false;
        var left = predicate.Left is null ? null : Normalize(predicate.Left, ref localChanged);
        var right = predicate.Right is null ? null : Normalize(predicate.Right, ref localChanged);
        var current = predicate with { Left = left, Right = right };
        var beforeKey = StructuralKey(predicate);

        if (current.Kind == AuthorizationPredicateKind.Not &&
            current.Left?.Kind == AuthorizationPredicateKind.Not &&
            current.Left.Left is not null)
        {
            current = current.Left.Left;
            localChanged = true;
        }
        else if (current.Kind is AuthorizationPredicateKind.And or AuthorizationPredicateKind.Or)
        {
            var operands = new List<AuthorizationPredicate>();
            Flatten(current.Kind, current, operands);
            operands = operands
                .GroupBy(StructuralKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(StructuralKey, StringComparer.Ordinal)
                .ToList();

            current = operands.Count == 1
                ? operands[0]
                : RebuildBalanced(current.Kind, operands);
        }

        var afterKey = StructuralKey(current);
        if (!StringComparer.Ordinal.Equals(beforeKey, afterKey))
        {
            changed = true;
            return current;
        }

        // Structural equivalence means this node is already canonical. Return
        // the original object so the rule is genuinely idempotent.
        return predicate;
    }

    private static void Flatten(AuthorizationPredicateKind kind, AuthorizationPredicate node,
        ICollection<AuthorizationPredicate> destination)
    {
        if (node.Kind == kind)
        {
            if (node.Left is not null) Flatten(kind, node.Left, destination);
            if (node.Right is not null) Flatten(kind, node.Right, destination);
            return;
        }

        destination.Add(node);
    }

    private static AuthorizationPredicate RebuildBalanced(AuthorizationPredicateKind kind,
        IReadOnlyList<AuthorizationPredicate> operands)
    {
        AuthorizationPredicate result = operands[0];
        for (var i = 1; i < operands.Count; i++)
            result = kind == AuthorizationPredicateKind.And
                ? AuthorizationPredicate.And(result, operands[i])
                : AuthorizationPredicate.Or(result, operands[i]);
        return result;
    }

    private static string StructuralKey(AuthorizationPredicate predicate)
    {
        var builder = new StringBuilder(128);
        AppendKey(builder, predicate);
        return builder.ToString();
    }

    private static void AppendKey(StringBuilder builder, AuthorizationPredicate predicate)
    {
        builder.Append((byte)predicate.Kind).Append('(');
        AppendValue(builder, predicate.Name);
        builder.Append('|');
        AppendValue(builder, predicate.Value);
        builder.Append('|');
        if (predicate.Left is not null) AppendKey(builder, predicate.Left);
        builder.Append('|');
        if (predicate.Right is not null) AppendKey(builder, predicate.Right);
        builder.Append(')');
    }

    private static void AppendValue(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append(value.Length).Append(':').Append(value);
    }
}