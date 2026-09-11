using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

/// <summary>
/// The manual model is deliberately a tiny semantic overlay, not a second copy of
/// the complete metadata-discovered Supply Chain schema.
/// </summary>
public sealed class ManualSemanticModelTests
{
    [Fact]
    public void Manual_model_contains_only_the_two_entities_used_by_the_overlay()
    {
        var model = ManualSupplyChainSemanticModel.Model;

        Assert.Equal(2, model.Entities.Count);
        Assert.Contains(model.Entities, e => e.Name == "Product");
        Assert.Contains(model.Entities, e => e.Name == "ProductComponent");
        Assert.DoesNotContain(model.Entities, e => e.Name == "Supplier");
        Assert.DoesNotContain(model.Entities, e => e.Name == "PurchaseOrder");
    }

    [Fact]
    public void Typed_manual_model_exercises_aliases_constraints_and_relationships()
    {
        var model = ManualSupplyChainSemanticModel.Model;
        var product = model.Get(ManualSupplyChainSemanticModel.Product);
        var component = model.Get(ManualSupplyChainSemanticModel.ProductComponent);

        Assert.Contains(product.EffectiveAliases, a => a.Name == "Item");
        Assert.Contains(product.EffectiveAliases, a => a.Name == "Item2");

        var sku = product.Fields.Single(f => f.Name == "Sku");
        Assert.Contains(sku.EffectiveAliases, a => a.Name == "PartNumber");
        Assert.Contains(sku.EffectiveConstraints, c => c.Kind == SemanticConstraintKind.Pattern);

        var safetyStock = product.Fields.Single(f => f.Name == "SafetyStock");
        Assert.True(safetyStock.Capabilities.HasFlag(SemanticFieldCapabilities.Writable));
        Assert.Contains(safetyStock.EffectiveConstraints, c => c.Kind == SemanticConstraintKind.Range);

        Assert.Contains(product.Relationships, r => r.Name == "components" && r.Target == component.Id);
        Assert.Contains(component.Relationships, r => r.Name == "componentProduct" && r.Target == product.Id);
    }

    [Fact]
    public void Untyped_semantic_entities_are_first_class_and_do_not_require_a_clr_model()
    {
        var entityId = EntityId.Create("IntentOnly");
        var fieldId = FieldId.Create("IntentOnly", "ExternalId");

        var model = new SemanticModelBuilder()
            .Entity(entityId, "IntentOnly", e => e
                .Identity(fieldId, "ExternalId")
                .Field(fieldId, "ExternalId", typeof(string))
                .Alias("intent-only"))
            .Build();

        var entity = model.Get(entityId);
        Assert.Null(entity.ModelType);
        Assert.Equal("ExternalId", entity.Identity.Name);
        Assert.Contains(entity.EffectiveAliases, a => a.Name == "intent-only");
    }

    [Fact]
    public void Manual_model_can_be_composed_with_the_generated_model()
    {
        var manual = ManualSupplyChainSemanticModel.Model;
        var generated = SupplyChainSemanticModel.Model;

        Assert.NotSame(manual, generated);
        Assert.Equal(2, manual.Entities.Count);
        Assert.Equal(17, generated.Entities.Count);
        Assert.Contains(generated.Entities, entity => entity.Name == "Product");
        Assert.Contains(generated.Entities, entity => entity.Name == "ProductComponent");

        var frozen = generated.Freeze();
        Assert.True(frozen.IsFrozen);
        frozen.CreateSnapshot();
    }

}