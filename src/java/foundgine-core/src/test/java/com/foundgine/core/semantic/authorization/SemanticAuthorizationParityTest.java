package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.resolution.*;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/** Port of Foundgine.Semantics.Tests.SemanticAuthorizationTests. */
class SemanticAuthorizationParityTest {
	@Test
	void contractAwareAuthorizationAcceptsOperationFromSameContract() {
		var b = banking();
		var c = snapshot(b.model);
		var op = compile(c, b.request);
		var authorized = new SemanticAuthorizer(new DenyAccountPolicy()).authorize(c, op);
		assertEquals(b.customer, authorized.root().entityId());
	}

	@Test
	void contractAwareAuthorizationRejectsUnknownEntityBeforePolicyEvaluation() {
		var b = banking();
		var c = snapshot(b.model);
		var op = compile(c, b.request);
		var r = op.root();
		var tampered = new SemanticOperation(
				new SemanticReadNode(r.id(), new EntityId(999), r.fields(), r.viaRelationship(), r.viaConnection(),
						r.children(), r.queryOptions(), r.authorization(), r.requiredFields()));
		var ex = assertThrows(IllegalStateException.class,
				() -> new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).authorize(c, tampered));
		assertTrue(ex.getMessage().contains("999"));
	}

	@Test
	void contractAwareAuthorizationRejectsRelationshipTargetMismatchBeforePolicyEvaluation() {
		var b = banking();
		var c = snapshot(b.model);
		var op = compile(c, b.request);
		var child = op.root().children().get(0);
		var badChild = new SemanticReadNode(child.id(), b.customer, child.fields(), child.viaRelationship(),
				child.viaConnection(), child.children(), child.queryOptions(), child.authorization(),
				child.requiredFields());
		var root = op.root();
		var bad = new SemanticOperation(new SemanticReadNode(root.id(), root.entityId(), root.fields(),
				root.viaRelationship(), root.viaConnection(), List.of(badChild), root.queryOptions(),
				root.authorization(), root.requiredFields()));
		var ex = assertThrows(IllegalStateException.class,
				() -> new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()).authorize(c, bad));
		assertTrue(ex.getMessage().contains("target"));
		assertEquals(b.account, child.entityId());
	}

	@Test
	void authorizationCanBeAppliedToCanonicalSemanticIr() {
		var b = banking();
		var graph = new SemanticRequestResolver(snapshot(b.model)).resolve(b.request);
		var authorized = new SemanticAuthorizer(new DenyAccountPolicy())
				.authorize(SemanticOperationCompiler.compile(graph));
		assertEquals(b.customer, authorized.root().entityId());
		assertTrue(authorized.root().children().isEmpty());
	}

	@Test
	void deniedFieldIsRemovedFromAuthorizedGraph() {
		var b = banking();
		var graph = new SemanticRequestResolver(snapshot(b.model)).resolve(b.request);
		var authorized = new SemanticAuthorizer(new DenyBalancePolicy()).authorize(graph);
		assertEquals(3, authorized.nodes().size());
		assertEquals(List.of(new FieldId(1), new FieldId(2)), authorized.nodes().get(0).fields());
		assertEquals(List.of(new FieldId(1)), authorized.nodes().get(1).fields());
		assertFalse(authorized.nodes().get(1).fields().contains(new FieldId(3)));
	}

	@Test
	void denyingEveryRequestedFieldDoesNotReintroduceFields() {
		var b = banking();
		var graph = new SemanticRequestResolver(snapshot(b.model)).resolve(b.request);
		var authorized = new SemanticAuthorizer(new DenyAllCustomerFieldsPolicy()).authorize(graph);
		assertTrue(authorized.nodes().get(0).fields().isEmpty());
	}

	@Test
	void deniedRelationshipRemovesRelationshipSubtree() {
		var b = banking();
		var graph = new SemanticRequestResolver(snapshot(b.model)).resolve(b.request);
		var authorized = new SemanticAuthorizer(new DenyTransactionsPolicy()).authorize(graph);
		assertEquals(2, authorized.nodes().size());
		assertEquals(b.customer, authorized.nodes().get(0).entityId());
		assertEquals(b.account, authorized.nodes().get(1).entityId());
		assertTrue(authorized.nodes().stream().noneMatch(n -> n.entityId().equals(b.transaction)));
	}

	@Test
	void deniedChildEntityRemovesThatSubtree() {
		var b = banking();
		var graph = new SemanticRequestResolver(snapshot(b.model)).resolve(b.request);
		var authorized = new SemanticAuthorizer(new DenyAccountPolicy()).authorize(graph);
		assertEquals(1, authorized.nodes().size());
		assertEquals(b.customer, authorized.nodes().get(0).entityId());
		assertTrue(authorized.nodes().stream().noneMatch(n -> n.entityId().equals(b.account)));
	}

	@Test
	void deniedRootEntityRejectsRequest() {
		var b = banking();
		var graph = new SemanticRequestResolver(snapshot(b.model)).resolve(b.request);
		var ex = assertThrows(SemanticAuthorizationException.class,
				() -> new SemanticAuthorizer(new DenyCustomerPolicy()).authorize(graph));
		assertTrue(ex.getMessage().contains("Access denied"));
	}

	private static SemanticContractSnapshot snapshot(SemanticModel m) {
		return new SemanticContractSnapshot(m.freeze());
	}

	private static SemanticOperation compile(SemanticContractSnapshot c, SemanticRequest r) {
		return SemanticOperationCompiler.compile(new SemanticRequestResolver(c).resolve(r));
	}

	private record Banking(SemanticModel model, SemanticRequest request, EntityId customer, EntityId account,
			EntityId transaction) {
	}

	private static Banking banking() {
		var customer = new EntityId(1);
		var account = new EntityId(2);
		var transaction = new EntityId(3);
		var model = new SemanticModelBuilder()
				.entity(customer, "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class)
								.relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.MANY))
				.entity(account, "Account", e -> e.identity(new FieldId(1), "Id")
						.field(new FieldId(3), "Balance", java.math.BigDecimal.class)
						.relationship(new RelationshipId(2), "Transactions", transaction, RelationshipCardinality.MANY))
				.entity(transaction, "Transaction", e -> e.identity(new FieldId(1), "Id").field(new FieldId(3),
						"Amount", java.math.BigDecimal.class))
				.build();
		var tx = List.of(new SemanticSelection(new FieldId(1), null, List.of()));
		var accounts = List.of(new SemanticSelection(new FieldId(1), null, List.of()),
				new SemanticSelection(new FieldId(3), null, List.of()),
				new SemanticSelection(null, new RelationshipId(2), tx));
		var selections = List.of(new SemanticSelection(new FieldId(1), null, List.of()),
				new SemanticSelection(new FieldId(2), null, List.of()),
				new SemanticSelection(null, new RelationshipId(1), accounts));
		var request = new SemanticRequest(customer, selections, null, null);
		return new Banking(model, request, customer, account, transaction);
	}

	private static class DenyBalancePolicy extends AllowAllSemanticAuthorizationPolicy {
		public boolean canAccessField(EntityId e, FieldId f) {
			return !(e.equals(new EntityId(2)) && f.equals(new FieldId(3)));
		}
	}

	private static class DenyAllCustomerFieldsPolicy extends AllowAllSemanticAuthorizationPolicy {
		public boolean canAccessField(EntityId e, FieldId f) {
			return !e.equals(new EntityId(1));
		}
	}

	private static class DenyTransactionsPolicy extends AllowAllSemanticAuthorizationPolicy {
		public boolean canAccessRelationship(EntityId e, RelationshipId r) {
			return !(e.equals(new EntityId(2)) && r.equals(new RelationshipId(2)));
		}
	}

	private static class DenyAccountPolicy extends AllowAllSemanticAuthorizationPolicy {
		public boolean canAccessEntity(EntityId e) {
			return !e.equals(new EntityId(2));
		}
	}

	private static class DenyCustomerPolicy extends AllowAllSemanticAuthorizationPolicy {
		public boolean canAccessEntity(EntityId e) {
			return !e.equals(new EntityId(1));
		}
	}
}
