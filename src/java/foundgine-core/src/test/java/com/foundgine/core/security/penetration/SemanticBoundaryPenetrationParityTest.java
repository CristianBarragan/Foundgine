package com.foundgine.core.security.penetration;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.authorization.*;
import com.foundgine.core.semantic.ir.SemanticReadNode;
import com.foundgine.core.semantic.planning.Planner;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanFingerprint;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Penetration-style parity tests for the agent -> semantic model ->
 * authorization -> planner boundary. Inputs represent hostile model/MCP output
 * and must fail closed before provider execution.
 */
class SemanticBoundaryPenetrationParityTest {

	@Test
	void deniedRootEntityCannotReachPlanner() {
		var graph = new SemanticGraph();
		graph.addRoot(new EntityId(1), List.of(new FieldId(1)));

		var authorizer = new SemanticAuthorizer(new DenyAllPolicy());

		assertThrows(SemanticAuthorizationException.class, () -> authorizer.authorize(graph));
	}

	@Test
	void deniedFieldIsRemovedBeforePlanning() {
		var graph = new SemanticGraph();
		graph.addRoot(new EntityId(1), List.of(new FieldId(1), new FieldId(2)));

		var authorized = new SemanticAuthorizer(new FieldAllowPolicy()).authorize(graph);
		var plan = new Planner().plan(new com.foundgine.core.semantic.ir.SemanticOperation(new SemanticReadNode(0,
				new EntityId(1), List.of(new FieldId(1), new FieldId(2)), null, null, List.of(), null, null)));

		// Plan the authorized graph through the same canonical operation boundary used
		// by the runtime.
		var authorizedNode = authorized.nodes().get(0);
		var authorizedOperation = new com.foundgine.core.semantic.ir.SemanticOperation(
				new SemanticReadNode(authorizedNode.id(), authorizedNode.entityId(), authorizedNode.fields(),
						authorizedNode.viaRelationship(), authorizedNode.viaConnection(), List.of(),
						authorized.options(), authorizedNode.authorization()));
		var authorizedPlan = new Planner().plan(authorizedOperation);

		assertEquals(List.of(new FieldId(2)), authorizedPlan.root().fields());
	}

	@Test
	void deniedRelationshipRemovesEntireDescendantSubtree() {
		var grandchild = new SemanticReadNode(2, new EntityId(3), List.of(new FieldId(3)), new RelationshipId(11), null,
				List.of(), null, null);
		var child = new SemanticReadNode(1, new EntityId(2), List.of(new FieldId(2)), new RelationshipId(10), null,
				List.of(grandchild), null, null);
		var operation = new com.foundgine.core.semantic.ir.SemanticOperation(new SemanticReadNode(0, new EntityId(1),
				List.of(new FieldId(1)), null, null, List.of(child), null, null));

		var authorized = new SemanticAuthorizer(new RootOnlyPolicy()).authorize(operation);
		var plan = new Planner().plan(authorized);

		assertTrue(plan.root().children().isEmpty());
	}

	@Test
	void authorizationPredicateSurvivesSemanticPlanning() {
		var predicate = AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
				AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "TenantId"));

		var operation = new com.foundgine.core.semantic.ir.SemanticOperation(new SemanticReadNode(0, new EntityId(1),
				List.of(new FieldId(1)), null, null, List.of(), null, predicate));

		var authorized = new SemanticAuthorizer(new PredicatePolicy(predicate)).authorize(operation);
		var plan = new Planner().plan(authorized);

		assertEquals(predicate, plan.root().authorization());
	}

	@Test
	void authorizationPredicateChangesPlanFingerprint() {
		var a = AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
				AuthorizationPredicate.constant("1"));
		var b = AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
				AuthorizationPredicate.constant("2"));

		var operationA = new com.foundgine.core.semantic.ir.SemanticOperation(
				new SemanticReadNode(0, new EntityId(1), List.of(new FieldId(1)), null, null, List.of(), null, a));
		var operationB = new com.foundgine.core.semantic.ir.SemanticOperation(
				new SemanticReadNode(0, new EntityId(1), List.of(new FieldId(1)), null, null, List.of(), null, b));

		var planner = new Planner();
		var planA = planner.plan(new SemanticAuthorizer(new PredicatePolicy(a)).authorize(operationA));
		var planB = planner.plan(new SemanticAuthorizer(new PredicatePolicy(b)).authorize(operationB));

		assertNotEquals(SemanticPlanFingerprint.create(planA), SemanticPlanFingerprint.create(planB));
		assertNotEquals(SemanticPlanFingerprint.createShapeKey(planA), SemanticPlanFingerprint.createShapeKey(planB));
	}

	private static class DenyAllPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public boolean canAccessEntity(EntityId entityId) {
			return false;
		}
	}

	private static class FieldAllowPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public boolean canAccessField(EntityId entityId, FieldId fieldId) {
			return fieldId.equals(new FieldId(2));
		}
	}

	private static class RootOnlyPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public boolean canAccessEntity(EntityId entityId) {
			return entityId.equals(new EntityId(1));
		}

		@Override
		public boolean canAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) {
			return false;
		}
	}

	private record PredicatePolicy(AuthorizationPredicate predicate) implements ISemanticAuthorizationPolicy {

		@Override
		public boolean canAccessEntity(EntityId entityId) {
			return true;
		}

		@Override
		public boolean canAccessField(EntityId entityId, FieldId fieldId) {
			return true;
		}

		@Override
		public boolean canAccessRelationship(EntityId entityId, RelationshipId relationshipId) {
			return true;
		}

		@Override
		public boolean canWriteEntity(EntityId entityId) {
			return true;
		}

		@Override
		public boolean canWriteField(EntityId entityId, FieldId fieldId) {
			return true;
		}

		@Override
		public boolean canWriteRelationship(EntityId entityId, RelationshipId relationshipId) {
			return true;
		}

		@Override
		public AuthorizationPredicate getPredicate(EntityId entityId, AuthorizationOperation operation) {
			return operation == AuthorizationOperation.READ ? predicate : null;
		}

		@Override
		public AuthorizationDecision getEntityAccess(EntityId entityId, AuthorizationOperation operation) {
			return AuthorizationDecision.ALLOWED;
		}

		@Override
		public AuthorizationDecision getEntityAccess(EntityId entityId, AuthorizationOperation operation,
				AuthorizationOperationName operationName) {
			return AuthorizationDecision.ALLOWED;
		}

		@Override
		public AuthorizationDecision getFieldAccess(EntityId entityId, FieldId fieldId,
				AuthorizationOperation operation) {
			return AuthorizationDecision.ALLOWED;
		}

		@Override
		public AuthorizationDecision getRelationshipAccess(EntityId entityId, RelationshipId relationshipId,
				AuthorizationOperation operation) {
			return AuthorizationDecision.ALLOWED;
		}
	}
}
