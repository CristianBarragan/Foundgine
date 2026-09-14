package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.AuthorizationAccess;
import com.foundgine.core.abstractions.AuthorizationDecision;
import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationOperationName;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Tests.SemanticAuthorizationCapabilityCompositionTests}
 * and the accompanying {@code AuthorizationOperationNamePolicyTests}.
 */
class SemanticAuthorizationCapabilityCompositionParityTest {

	@Test
	void composeWithNoDecisionsIsAllowed() {
		var result = SemanticAuthorizationCapabilityComposition.compose();

		assertEquals(AuthorizationAccess.ALLOWED, result.access());
		assertNull(result.predicate());
	}

	@Test
	void composeIntersectsRatherThanUnions() {
		var result = SemanticAuthorizationCapabilityComposition.compose(AuthorizationDecision.ALLOWED,
				AuthorizationDecision.DENIED, AuthorizationDecision.ALLOWED);

		assertFalse(result.isAllowed());
	}

	@Test
	void composeAndsPredicatesAcrossEveryInput() {
		var tenantPredicate = AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
				AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "TenantId"));

		var ownerPredicate = AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "OwnerId"),
				AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "Id"));

		var result = SemanticAuthorizationCapabilityComposition.compose(
				AuthorizationDecision.conditional(tenantPredicate), AuthorizationDecision.conditional(ownerPredicate));

		assertEquals(AuthorizationAccess.CONDITIONAL, result.access());
		assertEquals(AuthorizationPredicate.and(tenantPredicate, ownerPredicate), result.predicate());
	}

	@Test
	void composeNeverWidensADenialRegardlessOfInputOrder() {
		var denyFirst = SemanticAuthorizationCapabilityComposition.compose(AuthorizationDecision.DENIED,
				AuthorizationDecision.ALLOWED);
		var denyLast = SemanticAuthorizationCapabilityComposition.compose(AuthorizationDecision.ALLOWED,
				AuthorizationDecision.DENIED);

		assertFalse(denyFirst.isAllowed());
		assertFalse(denyLast.isAllowed());
	}

	private static final class CoarseOnlyPolicy implements ISemanticAuthorizationPolicy {
		@Override
		public boolean canAccessEntity(EntityId entityId) {
			return true;
		}

		@Override
		public boolean canAccessField(EntityId entityId, FieldId fieldId) {
			return true;
		}

		@Override
		public boolean canAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) {
			return true;
		}

		@Override
		public boolean canWriteEntity(EntityId entityId) {
			return true;
		}
	}

	private static final class NamedOperationPolicy implements ISemanticAuthorizationPolicy {
		@Override
		public boolean canAccessEntity(EntityId entityId) {
			return true;
		}

		@Override
		public boolean canAccessField(EntityId entityId, FieldId fieldId) {
			return true;
		}

		@Override
		public boolean canAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) {
			return true;
		}

		@Override
		public boolean canWriteEntity(EntityId entityId) {
			return true;
		}

		@Override
		public AuthorizationDecision getEntityAccess(EntityId entityId, AuthorizationOperation operation,
				AuthorizationOperationName name) {
			return name != null && "Invoice.Pay".equals(name.value()) ? AuthorizationDecision.DENIED
					: AuthorizationDecision.ALLOWED;
		}
	}

	@Test
	void defaultNamedOverloadFallsBackToCoarseDecision() {
		ISemanticAuthorizationPolicy policy = new CoarseOnlyPolicy();

		var decision = policy.getEntityAccess(new EntityId(1), AuthorizationOperation.WRITE,
				new AuthorizationOperationName("Invoice.Pay"));

		assertTrue(decision.isAllowed());
	}

	@Test
	void overriddenNamedOperationCanNarrowBeyondCoarseWriteGate() {
		ISemanticAuthorizationPolicy policy = new NamedOperationPolicy();

		var pay = policy.getEntityAccess(new EntityId(1), AuthorizationOperation.WRITE,
				new AuthorizationOperationName("Invoice.Pay"));
		var update = policy.getEntityAccess(new EntityId(1), AuthorizationOperation.WRITE,
				new AuthorizationOperationName("Invoice.Update"));

		assertFalse(pay.isAllowed());
		assertTrue(update.isAllowed());
	}
}
