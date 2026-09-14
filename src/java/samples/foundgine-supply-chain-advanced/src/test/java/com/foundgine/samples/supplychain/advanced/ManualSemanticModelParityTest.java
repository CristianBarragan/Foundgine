package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.*;
import com.foundgine.samples.supplychain.advanced.semantics.ManualSupplyChainSemanticModel;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** Content-level port of the C# ManualSemanticModelTests. */
class ManualSemanticModelParityTest {
    @Test
    void manualModelContainsOnlyOverlayEntities() {
        var model = ManualSupplyChainSemanticModel.MODEL;
        assertEquals(2, model.entities().size());
        assertTrue(model.entities().stream().anyMatch(e -> e.name().equals("Product")));
        assertTrue(model.entities().stream().anyMatch(e -> e.name().equals("ProductComponent")));
        assertFalse(model.entities().stream().anyMatch(e -> e.name().equals("Supplier")));
        assertFalse(model.entities().stream().anyMatch(e -> e.name().equals("PurchaseOrder")));
    }

    @Test
    void typedManualModelPreservesAliasesConstraintsCapabilitiesAndRelationships() {
        var model = ManualSupplyChainSemanticModel.MODEL;
        var product = model.get(ManualSupplyChainSemanticModel.PRODUCT);
        var component = model.get(ManualSupplyChainSemanticModel.PRODUCT_COMPONENT);

        assertTrue(product.effectiveAliases().stream().anyMatch(a -> a.name().equals("Item")));
        assertTrue(product.effectiveAliases().stream().anyMatch(a -> a.name().equals("Item2")));
        var sku = product.fields().stream().filter(f -> f.name().equals("Sku")).findFirst().orElseThrow();
        assertTrue(sku.effectiveAliases().stream().anyMatch(a -> a.name().equals("PartNumber")));
        assertTrue(sku.effectiveConstraints().stream().anyMatch(c -> c.kind() == SemanticConstraintKind.PATTERN));
        var safetyStock = product.fields().stream().filter(f -> f.name().equals("SafetyStock")).findFirst().orElseThrow();
        assertTrue((safetyStock.capabilities() & SemanticFieldCapabilities.WRITABLE) != 0);
        assertTrue(safetyStock.effectiveConstraints().stream().anyMatch(c -> c.kind() == SemanticConstraintKind.RANGE));
        assertTrue(product.relationships().stream().anyMatch(r -> r.name().equals("components") && r.target().equals(component.id())));
        assertTrue(component.relationships().stream().anyMatch(r -> r.name().equals("componentProduct") && r.target().equals(product.id())));
    }

    @Test
    void untypedSemanticEntitiesDoNotRequireClrModel() {
        var entityId = EntityId.create("IntentOnly");
        var fieldId = FieldId.create("IntentOnly", "ExternalId");
        var model = new SemanticModelBuilder()
                .entity(entityId, "IntentOnly", e -> e
                        .identity(fieldId, "ExternalId")
                        .field(fieldId, "ExternalId", String.class, null, SemanticFieldCapabilities.DEFAULT)
                        .alias("intent-only"))
                .build();
        var entity = model.get(entityId);
        assertNull(entity.modelType());
        assertEquals("ExternalId", entity.identity().name());
        assertTrue(entity.effectiveAliases().stream().anyMatch(a -> a.name().equals("intent-only")));
    }

    @Test
    void manualModelIsComposedWithoutBecomingTheGeneratedModel() {
        var manual = ManualSupplyChainSemanticModel.MODEL;
        var generated = SupplyChainSemanticModel.MODEL;
        assertNotSame(manual, generated);
        assertEquals(2, manual.entities().size());
        assertEquals(17, generated.entities().size());
        assertTrue(generated.entities().stream().anyMatch(e -> e.name().equals("Product")));
        assertTrue(generated.entities().stream().anyMatch(e -> e.name().equals("ProductComponent")));
        var frozen = generated.freeze();
        assertTrue(frozen.isFrozen());
        assertNotNull(frozen.createSnapshot());
    }
}
