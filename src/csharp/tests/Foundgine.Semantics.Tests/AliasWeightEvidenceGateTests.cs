using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;
using Xunit;

namespace Foundgine.Semantics.Tests;

// This suite intentionally exercises the obsolete IsConclusive/ModelWeight
// compatibility projections (in addition to their replacements, Status and
// ModelEvidence) to prove existing callers relying on them still compile and
// behave correctly. CS0618 is expected here and nowhere else in this file.
#pragma warning disable CS0618
public sealed class AliasWeightEvidenceGateTests
{
    [Fact]
    public void Weight_feature_is_inert_without_lexical_grounding()
    {
        var model = BuildModel(e => e.Alias("Vendor", 50).FieldAlias(x => x.State, "State", 50));

        var result = AliasWeightEvidenceGate.Evaluate(model, 90);

        Assert.Equal(AliasEvidenceStatus.NotApplicable, result.Status);
        Assert.True(result.IsConclusive);
        Assert.Equal(ModelResolutionEvidence.Unknown, result.ModelEvidence);
        Assert.Null(result.ModelWeight);
        Assert.Empty(result.EntityWeights);
        Assert.Empty(result.FieldWeights);
        Assert.False(string.IsNullOrEmpty(result.ContractFingerprint));
    }

    [Fact]
    public void Known_model_is_a_distinct_provenance_category_not_numeric_evidence()
    {
        var model = BuildModel(e => e.Alias("Vendor", 50));
        var resolution = ResolveEntity("Vendor", new EntityId(1), .91);

        var result = AliasWeightEvidenceGate.Evaluate(model, 90, resolution, modelKnownWithCertainty: true);

        Assert.Equal(AliasEvidenceStatus.Insufficient, result.Status);
        Assert.False(result.IsConclusive);
        Assert.Equal(ModelResolutionEvidence.KnownWithCertainty, result.ModelEvidence);
        Assert.Equal(100, result.ModelWeight); // compatibility projection only
        Assert.Equal(50, result.EntityWeights[new EntityId(1)]);
        Assert.Contains(new EntityId(1),
            result.ViolatingEntities); // entity evidence remains 50; model certainty does not inflate it
    }

    [Fact]
    public void Field_weight_is_scoped_to_the_field_and_never_becomes_entity_weight()
    {
        var model = BuildModel(e => e.FieldAlias(x => x.State, "State", 50));
        var resolution = ResolveField("State", new EntityId(1), FieldId.Create("Supplier", "State"), .95);

        var result = AliasWeightEvidenceGate.Evaluate(model, 80, resolution);

        Assert.False(result.IsConclusive);
        Assert.Empty(result.EntityWeights);
        Assert.Equal(50, result.FieldWeights[FieldId.Create("Supplier", "State")]);
        Assert.Empty(result.ViolatingEntities);
        Assert.Equal([FieldId.Create("Supplier", "State")], result.ViolatingFields);
    }

    [Fact]
    public void Entity_weight_is_scoped_to_the_entity()
    {
        var model = BuildModel(e => e.Alias("Vendor", 90));
        var resolution = ResolveEntity("Vendor", new EntityId(1), .95);

        var result = AliasWeightEvidenceGate.Evaluate(model, 80, resolution);

        Assert.Equal(AliasEvidenceStatus.Sufficient, result.Status);
        Assert.True(result.IsConclusive);
        Assert.Equal(90, result.EntityWeights[new EntityId(1)]);
    }

    [Fact]
    public void Relationship_weight_is_scoped_to_relationship()
    {
        var model = BuildModel(e => e.RelationshipAlias(new RelationshipId(12), "orders", 60));
        var candidate = new SemanticLexicalCandidate(
            "orders", SemanticLexicalCandidateKind.Relationship, "Orders", .9,
            RelationshipId: new RelationshipId(12), SourceEntityId: new EntityId(1), TargetEntityId: new EntityId(2));
        var resolution = new SemanticLexicalResolution(
            SemanticLexicalResolutionOutcome.Resolved,
            [new SemanticLexicalStep("orders", candidate, .9, [])], .9, new EntityId(1), null);

        var result = AliasWeightEvidenceGate.Evaluate(model, 80, resolution);

        Assert.False(result.IsConclusive);
        Assert.Empty(result.EntityWeights);
        Assert.Equal(60, result.RelationshipWeights[new RelationshipId(12)]);
        Assert.Equal([new RelationshipId(12)], result.ViolatingRelationships);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Alias_weight_accepts_inclusive_boundaries(int weight) =>
        Assert.Equal(weight, new SemanticAlias("Vendor", weight).Weight);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Alias_weight_rejects_outside_range(int weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticAlias("Vendor", weight));

    [Fact]
    public void Multiple_weighted_alias_steps_for_one_identity_use_the_strongest_declared_evidence()
    {
        var model = BuildModel(e => e.Alias("Vendor", 95).Alias("Seller", 40));
        var resolution = new SemanticLexicalResolution(
            SemanticLexicalResolutionOutcome.Resolved,
            [
                new SemanticLexicalStep("Vendor",
                    new SemanticLexicalCandidate("Vendor", SemanticLexicalCandidateKind.Entity, "Supplier", .81,
                        EntityId: new EntityId(1)), .81, []),
                new SemanticLexicalStep("Seller",
                    new SemanticLexicalCandidate("Seller", SemanticLexicalCandidateKind.Entity, "Supplier", .97,
                        EntityId: new EntityId(1)), .97, [])
            ],
            .89,
            new EntityId(1),
            null);

        var result = AliasWeightEvidenceGate.Evaluate(model, 90, resolution);

        Assert.Equal(AliasEvidenceStatus.Sufficient, result.Status);
        Assert.Equal(95, result.EntityWeights[new EntityId(1)]);
    }

    [Fact]
    public void Unweighted_alias_produces_no_evidence_rather_than_an_implicit_zero_weight()
    {
        // An alias declared with no weight is a different thing from an alias
        // declared with weight 0 (which the constructor rejects outright).
        // Matching an unweighted alias must not be treated as failing
        // evidence — it must not enter the evidence dictionaries at all.
        var model = BuildModel(e => e.Alias("Vendor"));
        var resolution = ResolveEntity("Vendor", new EntityId(1), .95);

        var result = AliasWeightEvidenceGate.Evaluate(model, 1, resolution);

        Assert.Equal(AliasEvidenceStatus.NotApplicable, result.Status);
        Assert.Empty(result.EntityWeights);
        Assert.Empty(result.ViolatingEntities);
    }

    [Fact]
    public void Weighted_and_unweighted_aliases_for_the_same_entity_only_the_weighted_one_contributes_evidence()
    {
        var model = BuildModel(e => e.Alias("Vendor").Alias("Seller", 60));

        // Two independent lexical steps: one hits the unweighted alias, the
        // other hits the weighted one. Evidence must come only from the step
        // that actually matched a weighted declaration.
        var resolution = new SemanticLexicalResolution(
            SemanticLexicalResolutionOutcome.Resolved,
            [
                new SemanticLexicalStep("Vendor",
                    new SemanticLexicalCandidate("Vendor", SemanticLexicalCandidateKind.Entity, "Supplier", .81,
                        EntityId: new EntityId(1)), .81, []),
                new SemanticLexicalStep("Seller",
                    new SemanticLexicalCandidate("Seller", SemanticLexicalCandidateKind.Entity, "Supplier", .70,
                        EntityId: new EntityId(1)), .70, [])
            ],
            .75,
            new EntityId(1),
            null);

        var result = AliasWeightEvidenceGate.Evaluate(model, 50, resolution);

        Assert.Equal(AliasEvidenceStatus.Sufficient, result.Status);
        Assert.Equal(60, result.EntityWeights[new EntityId(1)]);
    }

    [Fact]
    public void Canonical_name_step_and_weighted_alias_step_for_the_same_entity_only_the_alias_contributes_evidence()
    {
        // A candidate whose Token is the entity's own canonical name (not a
        // declared alias) must not spuriously match an alias lookup. Only the
        // step that actually used the declared alias should show up as
        // evidence.
        var model = BuildModel(e => e.Alias("Vendor", 70));

        var resolution = new SemanticLexicalResolution(
            SemanticLexicalResolutionOutcome.Resolved,
            [
                new SemanticLexicalStep("Supplier",
                    new SemanticLexicalCandidate("Supplier", SemanticLexicalCandidateKind.Entity, "Supplier", .99,
                        EntityId: new EntityId(1)), .99, []),
                new SemanticLexicalStep("Vendor",
                    new SemanticLexicalCandidate("Vendor", SemanticLexicalCandidateKind.Entity, "Supplier", .81,
                        EntityId: new EntityId(1)), .81, [])
            ],
            .90,
            new EntityId(1),
            null);

        var result = AliasWeightEvidenceGate.Evaluate(model, 60, resolution);

        Assert.Equal(AliasEvidenceStatus.Sufficient, result.Status);
        Assert.Equal(70, result.EntityWeights[new EntityId(1)]);
    }

    [Fact]
    public void Contract_fingerprint_identifies_the_exact_frozen_contract_the_evidence_was_measured_against()
    {
        var modelA = BuildModel(e => e.Alias("Vendor", 90));
        var modelB = BuildModel(e => e.Alias("Vendor", 40)); // different declared weight => different contract

        var resultA1 = AliasWeightEvidenceGate.Evaluate(modelA, 80, ResolveEntity("Vendor", new EntityId(1), .9));
        var resultA2 = AliasWeightEvidenceGate.Evaluate(modelA, 80, ResolveEntity("Vendor", new EntityId(1), .9));
        var resultB = AliasWeightEvidenceGate.Evaluate(modelB, 80, ResolveEntity("Vendor", new EntityId(1), .9));

        Assert.False(string.IsNullOrEmpty(resultA1.ContractFingerprint));
        Assert.Equal(resultA1.ContractFingerprint, resultA2.ContractFingerprint);
        Assert.NotEqual(resultA1.ContractFingerprint, resultB.ContractFingerprint);
    }

    private static SemanticModel BuildModel(Action<SemanticEntityBuilder<SupplierModel>> configure)
    {
        return new SemanticModelBuilder()
            .Entity<SupplierModel>(new EntityId(1), "Supplier", e =>
            {
                e.Identity(x => x.Id)
                    .Field(x => x.State)
                    .Relationship<OrderModel>(
                        new RelationshipId(12),
                        "Orders",
                        x => x.OrderId,
                        x => x.SupplierId,
                        new EntityId(2),
                        RelationshipCardinality.Many);
                configure(e);
            })
            .Entity<OrderModel>(new EntityId(2), "Order", e => e.Identity(x => x.Id))
            .Build()
            .Freeze();
    }

    private static SemanticLexicalResolution ResolveEntity(string token, EntityId entityId, double score)
    {
        var candidate = new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.Entity, "Supplier", score,
            EntityId: entityId);
        return new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.Resolved,
            [new SemanticLexicalStep(token, candidate, score, [])], score, entityId, null);
    }

    private static SemanticLexicalResolution ResolveField(string token, EntityId entityId, FieldId fieldId,
        double score)
    {
        var candidate = new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.Field, "Supplier State", score,
            EntityId: entityId, FieldId: fieldId);
        return new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.Resolved,
            [new SemanticLexicalStep(token, candidate, score, [])], score, entityId, null);
    }

    private sealed class SupplierModel
    {
        public int Id { get; set; }
        public string State { get; set; } = string.Empty;
        public int OrderId { get; set; }
    }

    private sealed class OrderModel
    {
        public int Id { get; set; }
        public int SupplierId { get; set; }
    }
}
#pragma warning restore CS0618