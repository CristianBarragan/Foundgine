using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Grounding;

/// <summary>
/// End-to-end contract coverage for weighted semantic aliases in the advanced
/// Supply Chain sample. Weights are evidence metadata, not retrieval scores and
/// never grant authority. The gate is deliberately tested separately from
/// lexical resolution so a low-confidence signal cannot silently become an
/// authorization decision.
/// </summary>
// Intentionally exercises the obsolete IsConclusive/ModelWeight compatibility
// projections (alongside their replacements, Status and ModelEvidence) to
// prove existing callers relying on them still compile and behave correctly.
#pragma warning disable CS0618
public sealed class SupplyChainAliasWeightTests
{
    [Fact]
    public void Aot_generated_supply_chain_contract_preserves_weighted_entity_field_and_relationship_aliases()
    {
        var contract = SupplyChainSemanticModel.Build().Freeze().CreateSnapshot();

        var supplier = contract.ResolveEntity("Supplier");
        Assert.Equal(
            [("Vendor", 95), ("Seller", 90)],
            supplier.EffectiveAliases.Select(a => (a.Name, a.Weight)));

        Assert.Equal(
            [("State", 85)],
            supplier.Fields.Single(x => x.Name == "Country").EffectiveAliases
                .Select(a => (a.Name, a.Weight)));

        var purchaseOrder = contract.ResolveEntity("PurchaseOrder");
        Assert.Equal(
            [("PO", 100), ("POs", 95), ("Buy", 90), ("Buys", 85)],
            purchaseOrder.EffectiveAliases.Select(a => (a.Name, a.Weight)));

        Assert.Equal(
            [("DueDate", 90)],
            purchaseOrder.Fields.Single(x => x.Name == "ExpectedArrival").EffectiveAliases
                .Select(a => (a.Name, a.Weight)));

        Assert.Equal(
            [("vendor", 85)],
            purchaseOrder.Relationships.Single(x => x.Name == "supplier").EffectiveAliases
                .Select(a => (a.Name, a.Weight)));
    }

    [Fact]
    public void Supply_chain_weighted_alias_evidence_only_counts_the_declared_lexical_identity()
    {
        var model = SupplyChainSemanticModel.Build().Freeze();

        var result = AliasWeightEvidenceGate.Evaluate(model, minimumWeight: 80,
            LexicalEntity("Vendor", SupplyChainSemanticModel.Supplier));

        Assert.Equal(AliasEvidenceStatus.Sufficient, result.Status);
        Assert.True(result.IsConclusive);
        Assert.Empty(result.ViolatingEntities);
        Assert.Equal(
            [SupplyChainSemanticModel.Supplier],
            result.EntityWeights.Keys.OrderBy(x => x.Value));
        Assert.Equal(95d, result.EntityWeights[SupplyChainSemanticModel.Supplier]);
    }

    [Fact]
    public void Supply_chain_weighted_example_covers_entity_field_and_relationship_aliases()
    {
        var model = SupplyChainSemanticModel.Build().Freeze();

        var supplier = model.Get(SupplyChainSemanticModel.Supplier);
        var purchaseOrder = model.Get(SupplyChainSemanticModel.PurchaseOrder);

        var weightedSupplierAliases =
            supplier.EffectiveAliases
                .Concat(supplier.Fields.SelectMany(x => x.EffectiveAliases))
                .Concat(supplier.Relationships.SelectMany(x => x.EffectiveAliases))
                .ToArray();

        var weightedPurchaseOrderAliases =
            purchaseOrder.EffectiveAliases
                .Concat(purchaseOrder.Fields.SelectMany(x => x.EffectiveAliases))
                .Concat(purchaseOrder.Relationships.SelectMany(x => x.EffectiveAliases))
                .ToArray();

        Assert.NotEmpty(weightedSupplierAliases);
        Assert.NotEmpty(weightedPurchaseOrderAliases);
        Assert.All(weightedSupplierAliases, alias => Assert.InRange(alias.Weight!.Value, 1, 100));
        Assert.All(weightedPurchaseOrderAliases, alias => Assert.InRange(alias.Weight!.Value, 1, 100));
    }

    [Fact]
    public void Lowering_the_threshold_changes_only_the_evidence_gate_not_the_alias_identity()
    {
        var model = SupplyChainSemanticModel.Build().Freeze();
        var supplier = model.ResolveEntity("Seller");

        var strict = AliasWeightEvidenceGate.Evaluate(model, minimumWeight: 100,
            LexicalEntity("Seller", SupplyChainSemanticModel.Supplier));
        var relaxed = AliasWeightEvidenceGate.Evaluate(model, minimumWeight: 80,
            LexicalEntity("Seller", SupplyChainSemanticModel.Supplier));

        Assert.Equal(AliasEvidenceStatus.Insufficient, strict.Status);
        Assert.Equal(AliasEvidenceStatus.Sufficient, relaxed.Status);
        Assert.False(strict.IsConclusive);
        Assert.True(relaxed.IsConclusive);
        Assert.Equal(SupplyChainSemanticModel.Supplier, supplier.Id);
        Assert.Contains(supplier.EffectiveAliases, a => a.Name == "Seller" && a.Weight == 90);
    }

    private static SemanticLexicalResolution LexicalField(string token, EntityId entityId, FieldId fieldId)
    {
        var candidate = new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.Field, token, .99,
            EntityId: entityId, FieldId: fieldId);
        return new SemanticLexicalResolution(
            SemanticLexicalResolutionOutcome.Resolved,
            [new SemanticLexicalStep(token, candidate, .99, [])], .99, entityId, null);
    }

    private static SemanticLexicalResolution LexicalEntity(string token, EntityId entityId)
    {
        var candidate = new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.Entity,
            token, .99, EntityId: entityId);
        return new SemanticLexicalResolution(
            SemanticLexicalResolutionOutcome.Resolved,
            [new SemanticLexicalStep(token, candidate, .99, [])],
            .99, entityId, null);
    }


    [Fact]
    public void Field_weight_does_not_become_supplier_entity_weight()
    {
        var model = SupplyChainSemanticModel.Build().Freeze();
        var field = model.ResolveEntity("Supplier").Fields.Single(x => x.Name == "Country");
        var result = AliasWeightEvidenceGate.Evaluate(
            model, 90, LexicalField("State", SupplyChainSemanticModel.Supplier, field.Id));

        Assert.False(result.IsConclusive);
        Assert.Empty(result.EntityWeights);
        Assert.Equal(85d, result.FieldWeights[field.Id]);
        Assert.Empty(result.ViolatingEntities);
    }

    [Fact]
    public void Certain_model_is_a_distinct_provenance_category_and_does_not_inflate_field_evidence()
    {
        var model = SupplyChainSemanticModel.Build().Freeze();
        var field = model.ResolveEntity("Supplier").Fields.Single(x => x.Name == "Country");
        var result = AliasWeightEvidenceGate.Evaluate(
            model, 90, LexicalField("State", SupplyChainSemanticModel.Supplier, field.Id),
            modelKnownWithCertainty: true);

        Assert.False(result.IsConclusive);
        Assert.Equal(ModelResolutionEvidence.KnownWithCertainty, result.ModelEvidence);
        Assert.Equal(100, result.ModelWeight); // compatibility projection only; not combined with field evidence below
        Assert.Equal(85d, result.FieldWeights[field.Id]);
        Assert.Empty(result.EntityWeights);
    }

    [Fact]
    public void Weight_is_inert_when_the_request_did_not_use_lexical_grounding()
    {
        var model = SupplyChainSemanticModel.Build().Freeze();
        var result = AliasWeightEvidenceGate.Evaluate(model, 100);

        Assert.Equal(AliasEvidenceStatus.NotApplicable, result.Status);
        Assert.True(result.IsConclusive);
        Assert.Null(result.ModelWeight);
        Assert.Empty(result.EntityWeights);
        Assert.Empty(result.FieldWeights);
        Assert.Empty(result.RelationshipWeights);
    }

    [Fact]
    public void Weight_is_evidence_only_and_does_not_create_a_second_semantic_identity()
    {
        var model = SupplyChainSemanticModel.Build().Freeze();
        var canonical = model.ResolveEntity("Supplier");
        var alias = model.ResolveEntity("seller");

        Assert.Equal(canonical.Id, alias.Id);
        Assert.Equal(canonical.Name, alias.Name);
    }
}
#pragma warning restore CS0618