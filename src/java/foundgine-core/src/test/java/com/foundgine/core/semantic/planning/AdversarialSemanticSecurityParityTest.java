package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.AuthorizationAccess;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.authorization.AllowAllSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.capabilities.SemanticCapability;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContract;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContractDiscovery;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Planning.Tests.AdversarialSemanticSecurityTests}.
 *
 * <p>
 * Provider-independent adversarial checks for the semantic security boundary:
 * these lock the invariants that must hold before SQL/GraphQL/MCP execution is
 * allowed to occur.
 */
class AdversarialSemanticSecurityParityTest {

	@Test
	void crossTenantPredicateIsPreservedAndCannotBeDroppedDuringDiscovery() {
		var model = model();
		var predicate = AuthorizationPredicate.equal(
				AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), "TenantId"),
				AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "TenantId"));

		var contract = SemanticCapabilityContractDiscovery.describe(model, new TenantPolicy(predicate));

		var capability = single(contract, x -> x.id().equals("Customer.read"));
		assertEquals(AuthorizationAccess.CONDITIONAL, capability.access().access());
		assertSame(predicate, capability.access().predicate());
	}

	@Test
	void hiddenFieldIsNotAdvertisedAsAnAgentCapability() {
		var model = model();
		var contract = SemanticCapabilityContractDiscovery.describe(model, new HiddenFieldPolicy());

		var read = single(contract, x -> x.id().equals("Customer.read"));
		assertTrue(read.fields().contains("Name"));
		assertFalse(read.fields().contains("Balance"));
	}

	@Test
	void unauthorizedRelationshipTraversalIsNotAdvertised() {
		var model = modelWithAccount();
		var contract = SemanticCapabilityContractDiscovery.describe(model, new DenyAccountTraversalPolicy());

		assertTrue(contract.capabilities().stream().noneMatch(x -> x.id().equals("Customer.accounts.traverse")));
	}

	@Test
	void capabilityContractMarksWritesAsSideEffectingAndNonIdempotentWhenAppropriate() {
		var contract = SemanticCapabilityContractDiscovery.describe(model(), new AllowAllSemanticAuthorizationPolicy());

		var create = single(contract, x -> x.id().equals("Customer.create"));
		assertTrue(create.hasSideEffects());
		assertFalse(create.isIdempotent());
		assertTrue(create.constraints().stream().anyMatch(x -> x.name().equals("writable-fields")));
	}

	private static SemanticCapability single(SemanticCapabilityContract contract,
			java.util.function.Predicate<SemanticCapability> predicate) {
		var matches = contract.capabilities().stream().filter(predicate).toList();
		assertEquals(1, matches.size(), "expected exactly one matching capability");
		return matches.get(0);
	}

	private static SemanticModel model() {
		return new SemanticModelBuilder().entity(new EntityId(1), "Customer",
				e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class)
						.field(new FieldId(3), "Balance", java.math.BigDecimal.class)
						.field(new FieldId(4), "TenantId", Integer.class))
				.build();
	}

	private static SemanticModel modelWithAccount() {
		return new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class).relationship(
								new RelationshipId(10), "accounts", new EntityId(2), RelationshipCardinality.MANY))
				.entity(new EntityId(2), "Account", e -> e.identity(new FieldId(1), "Id").field(new FieldId(2),
						"Balance", java.math.BigDecimal.class))
				.build();
	}

	private static final class TenantPolicy extends AllowAllSemanticAuthorizationPolicy {
		private final AuthorizationPredicate predicate;

		TenantPolicy(AuthorizationPredicate predicate) {
			this.predicate = predicate;
		}

		@Override
		public AuthorizationPredicate getPredicate(EntityId entityId, AuthorizationOperation operation) {
			return operation == AuthorizationOperation.READ ? predicate : null;
		}
	}

	private static final class HiddenFieldPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public boolean canAccessField(EntityId entityId, FieldId fieldId) {
			return !fieldId.equals(new FieldId(3));
		}
	}

	private static final class DenyAccountTraversalPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public boolean canAccessRelationship(EntityId sourceEntityId, RelationshipId relationshipId) {
			return !relationshipId.equals(new RelationshipId(10));
		}
	}
}
