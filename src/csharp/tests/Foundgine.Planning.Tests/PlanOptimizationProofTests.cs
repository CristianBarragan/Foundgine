using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class PlanOptimizationProofTests
{
    [Fact]
    public void OptimizationProof_RequiresAllPreservationDimensions()
    {
        var proof = new PlanOptimizationProof(
            "test-rule",
            SemanticMeaningPreserved: true,
            SecurityPreserved: true,
            AuthorizationBindingPreserved: true,
            EstimatedBenefit: 1d,
            EstimatedRewriteCost: 0.5d);

        Assert.True(proof.IsSatisfied);
    }

    [Fact]
    public void OptimizationProof_RejectsSemanticLoss()
    {
        var proof = new PlanOptimizationProof(
            "test-rule",
            SemanticMeaningPreserved: false,
            SecurityPreserved: true,
            AuthorizationBindingPreserved: true,
            EstimatedBenefit: 10d,
            EstimatedRewriteCost: 0d);

        Assert.False(proof.IsSatisfied);
    }

    [Fact]
    public void OptimizationProof_RejectsAuthorizationBindingLoss()
    {
        var proof = new PlanOptimizationProof(
            "test-rule",
            SemanticMeaningPreserved: true,
            SecurityPreserved: true,
            AuthorizationBindingPreserved: false,
            EstimatedBenefit: 10d,
            EstimatedRewriteCost: 0d);

        Assert.False(proof.IsSatisfied);
    }
}