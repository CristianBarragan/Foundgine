package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** Mirrors Foundgine.Semantics.Tests/SemanticAliasTests.cs. */
class SemanticAliasParityTest {
    @Test
    void entityAliasResolvesWithoutChangingIdentity() {
        var id = EntityId.create("Customer");
        var model = new SemanticModelBuilder()
                .entity(id, "Customer", e -> e
                        .alias("Client")
                        .identity(FieldId.create("Customer", "Id"), "Id"))
                .build();

        var entity = model.resolveEntity("client");
        assertEquals(id, entity.id());
        assertEquals("Customer", entity.name());
    }

    @Test
    void fieldAndRelationshipAliasesResolveToCanonicalDeclarations() {
        var customer = EntityId.create("Customer");
        var account = EntityId.create("Account");
        var field = FieldId.create("Customer", "Name");
        var relationship = RelationshipId.create("Customer", "Accounts");

        var model = new SemanticModelBuilder()
                .entity(customer, "Customer", e -> e
                        .identity(FieldId.create("Customer", "Id"), "Id")
                        .field(field, "Name", String.class)
                        .fieldAlias(field, "DisplayName")
                        .relationship(relationship, "Accounts", account, RelationshipCardinality.MANY)
                        .relationshipAlias(relationship, "CustomerAccounts"))
                .entity(account, "Account", e -> e.identity(FieldId.create("Account", "Id"), "Id"))
                .build();

        var entity = model.get(customer);
        assertEquals(field, entity.fields().stream()
                .filter(x -> x.effectiveAliases().stream().anyMatch(a -> a.name().equals("DisplayName")))
                .findFirst().orElseThrow().id());
        assertEquals(relationship, entity.relationships().stream()
                .filter(x -> x.effectiveAliases().stream().anyMatch(a -> a.name().equals("CustomerAccounts")))
                .findFirst().orElseThrow().id());
    }

    @Test
    void aliasCollisionBetweenEntitiesIsRejected() {
        var first = EntityId.create("Customer");
        var second = EntityId.create("Account");

        assertThrows(IllegalStateException.class, () -> new SemanticModelBuilder()
                .entity(first, "Customer", e -> e
                        .alias("Party")
                        .identity(FieldId.create("Customer", "Id"), "Id"))
                .entity(second, "Account", e -> e
                        .alias("Party")
                        .identity(FieldId.create("Account", "Id"), "Id"))
                .build());
    }
}
