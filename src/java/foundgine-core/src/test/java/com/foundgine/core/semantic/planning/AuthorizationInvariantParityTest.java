package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of {@code AuthorizationInvariantTests} (Foundgine.Planning.Tests).
 * Locks the provider-independent semantic-to-plan authorization boundary.
 */
class AuthorizationInvariantParityTest {

    @Test
    void deniedRootNeverProducesAnExecutionPlan() {
        var graph = new SemanticGraph();
        graph.addRoot(new EntityId(1), java.util.List.of(new FieldId(1)));

        var exception = assertThrows(SemanticAuthorizationException.class,
                () -> new SemanticAuthorizer(new DenyRootPolicy()).authorize(graph));

        assertTrue(exception.getMessage().contains("Access denied"));
    }

    @Test
    void deniedFieldCannotAppearInTheExecutionPlan() {
        var graph = new SemanticGraph();
        graph.addRoot(new EntityId(1), java.util.List.of(new FieldId(1), new FieldId(2)));

        var authorized = new SemanticAuthorizer(new DenyFieldPolicy()).authorize(graph);
        var plan = new Planner().plan(new com.foundgine.core.semantic.ir.SemanticOperation(
                new com.foundgine.core.semantic.ir.SemanticReadNode(
                        authorized.nodes().get(0).id(),
                        authorized.nodes().get(0).entityId(),
                        authorized.nodes().get(0).fields(),
                        authorized.nodes().get(0).viaRelationship(),
                        authorized.nodes().get(0).viaConnection(),
                        java.util.List.of(),
                        null,
                        authorized.nodes().get(0).authorization(),
                        java.util.List.of())));

        assertEquals(java.util.List.of(new FieldId(1)), plan.root().fields());
        assertFalse(plan.root().fields().contains(new FieldId(2)));
    }

    @Test
    void deniedRelationshipCannotAppearAsAnExecutionTraversal() {
        var graph = new SemanticGraph();
        var root = graph.addRoot(new EntityId(1), java.util.List.of(new FieldId(1)));
        graph.add(new EntityId(2), new RelationshipId(10), root, java.util.List.of(new FieldId(1)));

        var authorized = new SemanticAuthorizer(new DenyRelationshipPolicy()).authorize(graph);
        assertEquals(1, authorized.nodes().size());
        assertNull(authorized.nodes().get(0).viaRelationship());
    }

    @Test
    void conditionalAuthorizationIsPreservedInTheExecutionPlan() {
        var predicate = AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
                AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "TenantId"));

        var graph = new SemanticGraph();
        graph.addRoot(new EntityId(1), java.util.List.of(new FieldId(1)), predicate);

        var authorized = new SemanticAuthorizer(new ConditionalPolicy(predicate)).authorize(graph);
        var node = authorized.nodes().get(0);

        assertEquals(predicate, node.authorization());
    }

    private static final class DenyRootPolicy extends AllowAllSemanticAuthorizationPolicy {
        @Override
        public boolean canAccessEntity(EntityId entityId) {
            return false;
        }
    }

    private static final class DenyFieldPolicy extends AllowAllSemanticAuthorizationPolicy {
        @Override
        public boolean canAccessField(EntityId entityId, FieldId fieldId) {
            return !fieldId.equals(new FieldId(2));
        }
    }

    private static final class DenyRelationshipPolicy extends AllowAllSemanticAuthorizationPolicy {
        @Override
        public boolean canAccessRelationship(EntityId entityId, RelationshipId relationshipId) {
            return false;
        }
    }

    private static final class ConditionalPolicy extends AllowAllSemanticAuthorizationPolicy {
        private final AuthorizationPredicate predicate;

        private ConditionalPolicy(AuthorizationPredicate predicate) {
            this.predicate = predicate;
        }

        @Override
        public AuthorizationPredicate getPredicate(EntityId entityId, AuthorizationOperation operation) {
            return operation == AuthorizationOperation.READ ? predicate : null;
        }
    }
}
