using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Semantic.Query;

namespace Foundgine.Providers.Orm;

/// <summary>
/// Validates the IR against the generated capability set and binds the plan to its authorization provenance.
/// Unsupported shapes throw here, before any database work.
/// </summary>
public class OrmPlanCompiler : IProviderPlanCompiler, ISecurityInvariantProviderCompiler,
    IProviderSecurityConformanceEvaluator
{
    private readonly string _provider;
    private readonly OrmRegistry _registry;
    private readonly OrmProviderCapabilities _capabilities;

    public OrmPlanCompiler(
        string provider,
        OrmRegistry registry,
        OrmProviderCapabilities? capabilities = null)
    {
        _provider = string.IsNullOrWhiteSpace(provider)
            ? throw new ArgumentException("Provider name is required.", nameof(provider))
            : provider;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _capabilities = (capabilities ?? OrmProviderCapabilities.SharedLinq(provider)).ValidateFor(provider);
    }

    /// <summary>The exact capability profile used to reject unsupported plans before execution.</summary>
    public OrmProviderCapabilities Capabilities => _capabilities;

    public IReadOnlyCollection<string> PreservedSecurityInvariants =>
    [
        SecurityInvariantIds.AuthorizationRequired,
        SecurityInvariantIds.RuntimeAuthorization,
        SecurityInvariantIds.FieldVisibility,
        SecurityInvariantIds.RelationshipVisibility,
        SecurityInvariantIds.ParameterizedValues,
        SecurityInvariantIds.PlanCacheContextIsolation
    ];

    public ProviderPlan Compile(ExecutionIR ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ValidateTree(ir.Root, null);
        var plan = new OrmPlan(_provider, ir)
        {
            CapabilityContractVersion = _capabilities.ContractVersion,
            DeclaredFeatures = _capabilities.Features
        };
        ExecutionIRBoundary.BindProviderPlan(ir, plan);
        return plan;
    }

    private void ValidateTree(ExecutionIRNode node, ExecutionIRNode? parent)
    {
        var isRoot = parent is null;
        _capabilities.EnsureSupports(isRoot ? OrmProviderFeature.Scan : OrmProviderFeature.RelationshipTraversal);
        _capabilities.EnsureSupports(OrmProviderFeature.Projection);
        if (node.Authorization is not null)
            _capabilities.EnsureSupports(OrmProviderFeature.AuthorizationPredicates);
        if (node.QueryOptions is { Filter: not null } optionsWithFilter)
        {
            _capabilities.EnsureSupports(OrmProviderFeature.Filtering);
            EnsureFilterFeatures(optionsWithFilter.Filter!);
        }
        if (node.QueryOptions?.EffectiveOrder.Count > 0)
        {
            _capabilities.EnsureSupports(OrmProviderFeature.Ordering);
            if (node.QueryOptions.EffectiveOrder.Any(x => x.IsAggregate))
                _capabilities.EnsureSupports(OrmProviderFeature.AggregateOrdering);
        }
        if (!isRoot && node.QueryOptions is { } childOptions &&
            (childOptions.Limit is not null || childOptions.Offset is not null || childOptions.HasCursor))
            _capabilities.EnsureSupports(OrmProviderFeature.PerParentPagination);
        if (node.QueryOptions is { Limit: not null } or { Offset: not null })
            _capabilities.EnsureSupports(OrmProviderFeature.OffsetPagination);
        if (node.QueryOptions is { HasCursor: true })
            _capabilities.EnsureSupports(OrmProviderFeature.CursorPagination);

        _registry.GetMap(node.EntityId).Validate(node, isRoot);
        if (parent is not null)
        {
            var relationship = node.ViaRelationship
                ?? throw new InvalidOperationException($"Child execution node {node.Id} has no relationship identity.");
            var link = _registry.GetLink(relationship, parent.EntityId); // fails closed when keys are not mapped
            if (node.EntityId != link.ChildEntity)
                throw new InvalidOperationException(
                    $"Execution node {node.Id} targets entity '{node.EntityId.Value}', but relationship '{relationship.Value}' targets '{link.ChildEntity.Value}'.");
        }

        foreach (var child in node.Children) ValidateTree(child, node);
    }

    private void EnsureFilterFeatures(SemanticFilterExpression filter)
    {
        switch (filter)
        {
            case SemanticRelationshipFilter relationship:
                _capabilities.EnsureSupports(OrmProviderFeature.RelationshipFilters);
                EnsureFilterFeatures(relationship.Predicate);
                break;
            case SemanticAggregateFilter aggregate:
                _capabilities.EnsureSupports(OrmProviderFeature.AggregateFilters);
                if (aggregate.Predicate is not null) EnsureFilterFeatures(aggregate.Predicate);
                break;
            case SemanticAndFilter and:
                foreach (var child in and.Expressions) EnsureFilterFeatures(child);
                break;
            case SemanticOrFilter or:
                foreach (var child in or.Expressions) EnsureFilterFeatures(child);
                break;
        }
    }

    public ProviderSecurityConformanceResult Evaluate(ExecutionIR ir, ProviderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan is not OrmPlan ormPlan)
            throw new ArgumentException("Expected an OrmPlan.", nameof(plan));

        var required = ir.RequiredSecurityInvariants.Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var satisfied = required.Intersect(PreservedSecurityInvariants, StringComparer.Ordinal).ToArray();
        var violations = required.Except(satisfied, StringComparer.Ordinal)
            .Select(x => $"Invariant '{x}' is not certified by the {_provider} execution plan.").ToList();

        if (!string.Equals(ormPlan.Provider, _provider, StringComparison.Ordinal))
            violations.Add($"The plan belongs to provider '{ormPlan.Provider}', not '{_provider}'.");
        if (!string.Equals(ormPlan.CapabilityContractVersion, _capabilities.ContractVersion, StringComparison.Ordinal) ||
            ormPlan.DeclaredFeatures != _capabilities.Features)
            violations.Add($"The plan was not compiled against the active '{_provider}' capability contract.");
        if (!ReferenceEquals(ormPlan.IR, ir))
            violations.Add($"The {_provider} plan does not reference the supplied ExecutionIR instance.");
        if (ormPlan.AuthorizationBinding is null)
            violations.Add($"The {_provider} plan carries no authorization provenance.");
        else if (ormPlan.AuthorizationBinding != ir.AuthorizationBinding)
            violations.Add($"The {_provider} plan authorization provenance does not match the supplied ExecutionIR.");

        // Unknown invariant identifiers must never be treated as satisfied by omission.
        foreach (var invariant in required)
        {
            if (!Foundgine.Core.Semantic.Security.SecurityInvariantRegistry.Contains(invariant))
                violations.Add($"Required security invariant '{invariant}' is not registered.");
        }

        return new ProviderSecurityConformanceResult(_provider, required, satisfied, violations);
    }
}
