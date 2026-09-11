package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.ir.graph.*;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/** Port of Foundgine.Semantics.Tests.SemanticOperationGraphAuthorizationTests. */
class SemanticOperationGraphAuthorizationParityTest {
    @Test void graphAuthorizationReturnsOnlyAuthorizedSubgraph() {
        var contract = model();
        var authorized = new SemanticAuthorizer(new DenyTransactionsPolicy()).authorize(contract, SemanticOperationGraph.create(operation()));
        assertEquals(1, authorized.nodes().size());
        assertTrue(authorized.nodes().stream().noneMatch(n -> n.entityId().equals(EntityId.create("Transaction"))));
        assertEquals(authorized.rootId(), authorized.root().id());
    }
    @Test void graphAuthorizationEvidenceIsBoundToContract() {
        var contract = model();
        var result = new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).authorizeGraphWithEvidence(contract, SemanticOperationGraph.create(operation()));
        result.ensureMatches(contract);
        assertEquals(contract.contractFingerprint(), result.evidence().contractFingerprint());
    }
    private static SemanticContractSnapshot model() {
        var customer = EntityId.create("Customer"); var transaction = EntityId.create("Transaction"); var rel = RelationshipId.create("Customer", "Transactions");
        return new SemanticContractSnapshot(new SemanticModelBuilder()
            .entity(customer, "Customer", e -> e.identity(FieldId.create("Customer", "Id"), "Id").field(FieldId.create("Customer", "Id"), "Id", Long.class)
                .relationship(rel, "Transactions", transaction, RelationshipCardinality.MANY))
            .entity(transaction, "Transaction", e -> e.identity(FieldId.create("Transaction", "Id"), "Id").field(FieldId.create("Transaction", "Id"), "Id", Long.class))
            .build().freeze());
    }
    private static SemanticOperation operation() {
        var transaction = new SemanticReadNode(2, EntityId.create("Transaction"), List.of(FieldId.create("Transaction", "Id")), RelationshipId.create("Customer", "Transactions"), null, List.of(), null, null);
        var customer = new SemanticReadNode(1, EntityId.create("Customer"), List.of(FieldId.create("Customer", "Id")), null, null, List.of(transaction), null, null);
        return new SemanticOperation(customer);
    }
    private static final class DenyTransactionsPolicy extends AllowAllSemanticAuthorizationPolicy {
        @Override public boolean canAccessRelationship(EntityId source, RelationshipId relationship) {
            return !(source.equals(EntityId.create("Customer")) && relationship.equals(RelationshipId.create("Customer", "Transactions")));
        }
    }
}
