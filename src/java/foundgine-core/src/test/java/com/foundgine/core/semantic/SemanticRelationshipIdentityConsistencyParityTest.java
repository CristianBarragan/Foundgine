package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Mirrors SemanticRelationshipIdentityConsistencyTests.cs. */
class SemanticRelationshipIdentityConsistencyParityTest {
    @Test
    void sameRelationshipIdentityMustAgreeOnTarget() {
        var customer = EntityId.create("Customer");
        var order = EntityId.create("Order");
        var invoice = EntityId.create("Invoice");
        var relationship = RelationshipId.create("Customer", "Orders");
        var builder = new SemanticModelBuilder()
                .entity(customer, "Customer", e -> e.identity(FieldId.create("Customer", "Id"), "Id")
                        .relationship(relationship, "Orders", order, RelationshipCardinality.MANY))
                .entity(order, "Order", e -> e.identity(FieldId.create("Order", "Id"), "Id"))
                .entity(invoice, "Invoice", e -> e.identity(FieldId.create("Invoice", "Id"), "Id"));

        // A second declaration of the same relationship identity is expressed by an overlay.
        var conflicting = new SemanticModelBuilder()
                .entity(customer, "Customer", e -> e.identity(FieldId.create("Customer", "Id"), "Id")
                        .relationship(relationship, "Orders", invoice, RelationshipCardinality.MANY))
                .entity(invoice, "Invoice", e -> e.identity(FieldId.create("Invoice", "Id"), "Id"))
                .build();

        assertDoesNotThrow(builder::build);
        assertThrows(IllegalStateException.class, () -> builder.overlay(conflicting).build());
    }

    @Test
    void sameRelationshipIdentityMustAgreeOnCardinality() {
        var customer = EntityId.create("Customer");
        var order = EntityId.create("Order");
        var relationship = RelationshipId.create("Customer", "Orders");
        var first = new SemanticModelBuilder()
                .entity(customer, "Customer", e -> e.identity(FieldId.create("Customer", "Id"), "Id")
                        .relationship(relationship, "Orders", order, RelationshipCardinality.MANY))
                .entity(order, "Order", e -> e.identity(FieldId.create("Order", "Id"), "Id"))
                .build();
        var second = new SemanticModelBuilder()
                .entity(customer, "Customer", e -> e.identity(FieldId.create("Customer", "Id"), "Id")
                        .relationship(relationship, "Orders", order, RelationshipCardinality.ONE))
                .entity(order, "Order", e -> e.identity(FieldId.create("Order", "Id"), "Id"))
                .build();

        assertThrows(IllegalStateException.class, () -> new SemanticModelBuilder()
                .importModel(first).overlay(second).build());
    }
}
