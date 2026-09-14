using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Aggregates;
using Foundgine.Core.Semantic.Query;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Core.Semantic.Planning.Tests;

public sealed class AggregateRewriteProofTests
{
    [Fact]
    public void Self_substitution_with_known_provider_satisfies_every_dimension()
    {
        var plan = CreatePlan();

        var proof = AggregateRewriteProof.Create(
            plan,
            plan,
            SemanticAggregateSemanticsCatalog.Count,
            SemanticAggregateSemanticsCatalog.Count,
            AggregateCardinalityProof.FromCardinality(RelationshipCardinality.Many),
            AggregateProviderCapabilityRegistry.GenericSql,
            ProviderCostEstimate.From("sql", 1.0d));

        Assert.True(proof.IsSatisfied);
        Assert.True(proof.SemanticEquivalence.IsSatisfied);
        Assert.True(proof.EmptySetEquivalence.IsLegal);
        Assert.True(proof.NullEquivalence.IsLegal);
        Assert.True(proof.DuplicateEquivalence.IsLegal);
        Assert.True(proof.CardinalityProof.IsLegal);
        Assert.True(proof.AuthorizationPreservation.IsSatisfied);
    }

    [Fact]
    public void Count_to_min_substitution_is_rejected_even_with_known_cardinality_and_provider()
    {
        var plan = CreatePlan();

        var ex = Assert.Throws<InvalidOperationException>(() => AggregateRewriteProof.Create(
            plan,
            plan,
            SemanticAggregateSemanticsCatalog.Count,
            SemanticAggregateSemanticsCatalog.Min,
            AggregateCardinalityProof.FromCardinality(RelationshipCardinality.Many),
            AggregateProviderCapabilityRegistry.GenericSql,
            ProviderCostEstimate.From("sql", 1.0d)));

        Assert.Contains("empty-collection", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NULL-input", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate sensitivity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unregistered_provider_rejects_the_rewrite()
    {
        var plan = CreatePlan();
        var unknownProvider = new AggregateProviderCapability(
            "graphql-experimental",
            [SemanticFilterAggregate.Count],
            SupportsAggregatePredicate: false,
            SupportsRelationshipQuantifiers: false);

        var ex = Assert.Throws<InvalidOperationException>(() => AggregateRewriteProof.Create(
            plan,
            plan,
            SemanticAggregateSemanticsCatalog.Min,
            SemanticAggregateSemanticsCatalog.Min,
            AggregateCardinalityProof.FromCardinality(RelationshipCardinality.Many),
            unknownProvider,
            ProviderCostEstimate.From("graphql-experimental", 1.0d)));

        Assert.Contains("does not declare support for aggregate 'Min'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Semantic_equivalence_violation_is_reported_before_aggregate_specific_checks()
    {
        var before = CreatePlan(new FieldId(1));
        var after = CreatePlan(new FieldId(2));

        Assert.Throws<InvalidOperationException>(() => AggregateRewriteProof.Create(
            before,
            after,
            SemanticAggregateSemanticsCatalog.Count,
            SemanticAggregateSemanticsCatalog.Count,
            AggregateCardinalityProof.FromCardinality(RelationshipCardinality.Many),
            AggregateProviderCapabilityRegistry.GenericSql,
            ProviderCostEstimate.From("sql", 1.0d)));
    }

    [Fact]
    public void Security_contract_regression_is_rejected()
    {
        var before = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            [SecurityInvariantIds.AuthorizationRequired]);
        var after = new SemanticPlan(
            new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [new FieldId(1)], null, null, []),
            []);

        Assert.Throws<InvalidOperationException>(() => AggregateRewriteProof.Create(
            before,
            after,
            SemanticAggregateSemanticsCatalog.Count,
            SemanticAggregateSemanticsCatalog.Count,
            AggregateCardinalityProof.FromCardinality(RelationshipCardinality.Many),
            AggregateProviderCapabilityRegistry.GenericSql,
            ProviderCostEstimate.From("sql", 1.0d)));
    }


    [Fact]
    public void Manually_constructed_proof_is_not_satisfied_for_an_unsupported_target_aggregate()
    {
        var plan = CreatePlan();
        var unsupported = new AggregateProviderCapability(
            "count-only",
            [SemanticFilterAggregate.Count],
            SupportsAggregatePredicate: false,
            SupportsRelationshipQuantifiers: false);

        var proof = new AggregateRewriteProof(
            new SemanticEquivalenceProof("same", "same"),
            AggregateRewriteLegalityResult.Legal,
            AggregateRewriteLegalityResult.Legal,
            AggregateRewriteLegalityResult.Legal,
            AggregateRewriteLegalityResult.Legal,
            SemanticFilterAggregate.Min,
            unsupported,
            ProviderCostEstimate.From("count-only", 1d),
            AuthorizationPreservationProof.Create(plan, plan));

        Assert.False(proof.IsSatisfied);
    }

    private static SemanticPlan CreatePlan(FieldId? field = null) =>
        new(new SemanticPlanNode(1, ExecutionOperation.Scan, new EntityId(1), [field ?? new FieldId(1)], null, null,
            []));
}