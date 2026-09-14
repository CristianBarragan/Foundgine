package com.foundgine.samples.supplychain.advanced.semantics;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.samples.supplychain.advanced.domain.Domain;
import java.math.BigDecimal;

/** Small hand-authored semantic overlay matching the Advanced C# sample. */
public final class ManualSupplyChainSemanticModel {
    public static final EntityId PRODUCT = EntityId.create("Product");
    public static final EntityId PRODUCT_COMPONENT = EntityId.create("ProductComponent");
    public static final SemanticModel MODEL = build();

    public static SemanticModel build() {
        var builder = new SemanticModelBuilder();
        builder.entity(PRODUCT, "Product", e -> {
            e.aliases("Item", "Item2");
            e.identity(FieldId.create("Product", "Id"), "Id");
            field(e, "Product", "id", int.class, SemanticFieldCapabilities.DEFAULT);
            field(e, "Product", "sku", String.class, SemanticFieldCapabilities.DEFAULT);
            e.fieldAlias(FieldId.create("Product", "Sku"), "PartNumber");
            e.constraint(FieldId.create("Product", "Sku"), SemanticConstraint.pattern("^[A-Z0-9-]{3,32}$"));
            field(e, "Product", "name", String.class, SemanticFieldCapabilities.DEFAULT);
            field(e, "Product", "category", String.class, SemanticFieldCapabilities.DEFAULT);
            field(e, "Product", "safetyStock", BigDecimal.class, (byte)(SemanticFieldCapabilities.DEFAULT | SemanticFieldCapabilities.WRITABLE));
            e.constraint(FieldId.create("Product", "SafetyStock"), SemanticConstraint.range(BigDecimal.ZERO, null));
            e.relationship("components", PRODUCT_COMPONENT, RelationshipCardinality.MANY);
        });
        builder.entity(PRODUCT_COMPONENT, "ProductComponent", e -> {
            e.identity(FieldId.create("ProductComponent", "ParentProductId"), "ParentProductId");
            field(e, "ProductComponent", "parentProductId", int.class, SemanticFieldCapabilities.DEFAULT);
            field(e, "ProductComponent", "componentProductId", int.class, SemanticFieldCapabilities.DEFAULT);
            field(e, "ProductComponent", "quantityPerParent", BigDecimal.class, (byte)(SemanticFieldCapabilities.DEFAULT | SemanticFieldCapabilities.WRITABLE));
            e.constraint(FieldId.create("ProductComponent", "QuantityPerParent"), SemanticConstraint.range(BigDecimal.ZERO, null));
            e.relationship("componentProduct", PRODUCT, RelationshipCardinality.ONE);
        });
        return builder.build().freeze();
    }

    private static void field(SemanticEntityBuilder e, String entity, String javaName, Class<?> type, byte capabilities) {
        String semantic = Character.toUpperCase(javaName.charAt(0)) + javaName.substring(1);
        e.field(FieldId.create(entity, semantic), semantic, type, null, capabilities);
    }

    public static FieldId field(String entityName, String fieldName) {
        return MODEL.get(EntityId.create(entityName)).fields().stream()
                .filter(f -> f.name().equalsIgnoreCase(fieldName)).findFirst()
                .orElseThrow().id();
    }
    public static RelationshipId relationship(String entityName, String name) {
        return MODEL.get(EntityId.create(entityName)).relationships().stream()
                .filter(r -> r.name().equalsIgnoreCase(name)).findFirst()
                .orElseThrow().id();
    }
    private ManualSupplyChainSemanticModel() {}
}
