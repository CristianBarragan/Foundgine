using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class AuthorizationPreservationProofTests
{
    private static SemanticPlan Plan(params string[] invariants) => new(
        new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
        invariants);

    [Fact]
    public void Identical_invariant_sets_are_preserved()
    {
        var before = Plan(SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.TenantIsolation);
        var after = Plan(SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.TenantIsolation);

        var proof = AuthorizationPreservationProof.Create(before, after);

        Assert.True(proof.IsSatisfied);
        Assert.Empty(proof.Violations);
    }

    [Fact]
    public void Adding_an_invariant_is_not_a_regression()
    {
        var before = Plan(SecurityInvariantIds.AuthorizationRequired);
        var after = Plan(SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.TenantIsolation);

        var proof = AuthorizationPreservationProof.Create(before, after);

        Assert.True(proof.IsSatisfied);
    }

    [Fact]
    public void Dropping_a_required_invariant_is_rejected()
    {
        var before = Plan(SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.TenantIsolation);
        var after = Plan(SecurityInvariantIds.AuthorizationRequired);

        var proof = AuthorizationPreservationProof.Create(before, after);

        Assert.False(proof.IsSatisfied);
        Assert.Contains(proof.Violations,
            v => v.Contains(SecurityInvariantIds.TenantIsolation, StringComparison.Ordinal));
    }

    [Fact]
    public void Dropping_every_invariant_is_rejected_and_reports_each_one()
    {
        var before = Plan(SecurityInvariantIds.AuthorizationRequired, SecurityInvariantIds.TenantIsolation);
        var after = Plan();

        var proof = AuthorizationPreservationProof.Create(before, after);

        Assert.False(proof.IsSatisfied);
        Assert.Equal(2, proof.Violations.Count);
    }

    [Fact]
    public void No_required_invariants_on_either_side_is_trivially_preserved()
    {
        var before = Plan();
        var after = Plan();

        var proof = AuthorizationPreservationProof.Create(before, after);

        Assert.True(proof.IsSatisfied);
        Assert.Empty(proof.Violations);
    }
}