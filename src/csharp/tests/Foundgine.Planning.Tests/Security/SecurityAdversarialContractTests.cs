using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Security;
using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests.Security;

/// <summary>
/// M2 adversarial tests. These tests deliberately mutate or weaken the
/// security contract and assert that the earliest enforceable boundary rejects
/// the attack rather than merely producing a different result later.
/// </summary>
public sealed class SecurityAdversarialContractTests
{
    [Fact]
    public void Invariant_removal_is_rejected_by_rewrite_proof()
    {
        var before = SecuredPlan(SecurityInvariantIds.TenantIsolation);
        var after = before with
        {
            RequiredSecurityInvariants = before.EffectiveSecurityInvariants
                .Where(x => x != SecurityInvariantIds.TenantIsolation)
                .ToArray()
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityPreservationProof.Create(before, after));

        Assert.Contains(SecurityInvariantIds.TenantIsolation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invariant_weakening_is_rejected_when_tenant_is_replaced_by_generic_authorization()
    {
        var before = SecuredPlan(SecurityInvariantIds.TenantIsolation);
        var after = before with
        {
            RequiredSecurityInvariants = before.EffectiveSecurityInvariants
                .Where(x => x != SecurityInvariantIds.TenantIsolation)
                .Append(SecurityInvariantIds.AuthorizationRequired)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityPreservationProof.Create(before, after));

        Assert.Contains(SecurityInvariantIds.TenantIsolation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsafe_projection_rewrite_is_rejected_by_semantic_equivalence()
    {
        var before = SecuredPlan(SecurityInvariantIds.FieldVisibility);
        var after = new SemanticPlan(
                before.Root with
                {
                    Fields = [new FieldId(999)]
                })
            with
            {
                RequiredSecurityInvariants = before.EffectiveSecurityInvariants
            };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SemanticEquivalenceProof.Create(before, after));

        Assert.Contains("semantic meaning", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsafe_predicate_rewrite_is_rejected_even_when_invariants_are_unchanged()
    {
        var authorization = AuthorizationPredicate.Equal(
            AuthorizationPredicate.Member(AuthorizationPredicate.Parameter("row"), "tenant_id"),
            AuthorizationPredicate.ContextParameter("tenant_id"));
        var before = new SemanticPlan(new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(2)],
            null,
            null,
            [],
            Authorization: authorization));

        var weakenedAuthorization = AuthorizationPredicate.Constant("true");
        var after = before with
        {
            Root = before.Root with { Authorization = weakenedAuthorization },
            RequiredSecurityInvariants = before.EffectiveSecurityInvariants
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SemanticEquivalenceProof.Create(before, after));

        Assert.Contains("semantic meaning", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_constraint_loss_is_rejected_before_execution()
    {
        var required = new[]
        {
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.TenantIsolation,
            SecurityInvariantIds.ParameterizedValues
        };

        var proof = SecurityInvariantProof.Create(
            "adversarial-provider",
            required,
            [SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.ParameterizedValues]);

        Assert.False(proof.IsSatisfied);
        Assert.Contains(SecurityInvariantIds.TenantIsolation, proof.Missing);
        Assert.Throws<InvalidOperationException>(proof.EnsureSatisfied);
    }

    [Fact]
    public void Forged_provider_claim_cannot_add_unknown_invariant()
    {
        var matrix = new ProviderSecurityConformanceMatrix();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            matrix.Register(new ProviderSecurityConformanceProfile(
                "attacker-provider",
                ["security.tenant-bypass"],
                [])));

        Assert.Contains("security.tenant-bypass", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Proofless_provider_plan_is_rejected_at_execution_boundary()
    {
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [], null, null, []),
            [SecurityInvariantIds.AuthorizationRequired]);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(new UnprovedPlan(), ir));

        Assert.Contains("no security proof", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsatisfied_provider_plan_is_rejected_at_execution_boundary()
    {
        var proof = SecurityInvariantProof.Create(
            "adversarial-provider",
            [SecurityInvariantIds.TenantIsolation],
            []);
        var plan = new UnprovedPlan { SecurityProof = proof };

        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(1, ExecutionOperation.Scan, new EntityId(1), [], null, null, []),
            [SecurityInvariantIds.TenantIsolation]);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantExecutionGate.EnsureExecutable(plan, ir));

        Assert.Contains(SecurityInvariantIds.TenantIsolation, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capability_contract_rejects_unknown_invariant_before_planning()
    {
        var capability = new Foundgine.Core.Semantic.Capabilities.SemanticCapability(
            "Customer.read",
            "Read Customer",
            new EntityId(1),
            Foundgine.Core.Abstractions.AuthorizationDecision.Allowed,
            [], [], [], ["Name"], [])
        {
            RequiredSecurityInvariants = ["security.attacker-added"]
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityInvariantContractValidator.EnsureValid(capability));

        Assert.Contains("security.attacker-added", exception.Message, StringComparison.Ordinal);
    }

    private static SemanticPlan SecuredPlan(params string[] extraInvariants)
    {
        var node = new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            [new FieldId(2)],
            null,
            null,
            []);

        return SecurityInvariantPlanRequirements.Attach(
            new SemanticPlan(node),
            extraInvariants);
    }

    private sealed record UnprovedPlan() : ProviderPlan("adversarial-provider");
}