package com.foundgine.core.semantic.intent;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.ir.graph.*;
import com.foundgine.core.semantic.planning.*;
import com.foundgine.core.semantic.resolution.*;
import org.junit.jupiter.api.Test;
import java.math.BigDecimal;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/** Port of the core open-intent traversal parity cases. */
class OpenIntentTraversalParityTest {
	static final EntityId CUSTOMER = new EntityId(1), CUSTOMER_REL = new EntityId(2), CONTRACT = new EntityId(3),
			TRANSACTION = new EntityId(4);
	static final FieldId CUSTOMER_ID = new FieldId(1), AMOUNT = new FieldId(2);
	static final RelationshipId CUSTOMER_RELATIONSHIPS = new RelationshipId(10),
			RELATIONSHIP_CONTRACT = new RelationshipId(11), CONTRACT_TRANSACTIONS = new RelationshipId(12);

	@Test
	void dynamicLogicalTraversalExpandsToRealRelationshipChain() {
		var model = buildModel();
		var snapshot = new SemanticContractSnapshot(model.freeze());
		var intent = new ReadIntent("Customer",
				List.of(new ReadSelection(null, "transactions", List.of(new ReadSelection("Amount")))));
		var request = new ReadIntentCompiler(snapshot).compile(intent);
		var graph = new SemanticRequestResolver(snapshot).resolve(request);
		assertEquals(4, graph.nodes().size());
		assertEquals(CUSTOMER, graph.nodes().get(0).entityId());
		assertEquals(CUSTOMER_REL, graph.nodes().get(1).entityId());
		assertEquals(CONTRACT, graph.nodes().get(2).entityId());
		assertEquals(TRANSACTION, graph.nodes().get(3).entityId());
		assertEquals(CUSTOMER_RELATIONSHIPS, graph.nodes().get(1).viaRelationship());
		assertEquals(RELATIONSHIP_CONTRACT, graph.nodes().get(2).viaRelationship());
		assertEquals(CONTRACT_TRANSACTIONS, graph.nodes().get(3).viaRelationship());
		assertTrue(graph.nodes().get(3).fields().contains(AMOUNT));
	}

	@Test
	void dynamicIntentConvergesOnSameCanonicalOperationGraphAsTypedRequest() {
		var model = buildModel();
		var snapshot = new SemanticContractSnapshot(model.freeze());
		var intent = new ReadIntent("Customer",
				List.of(new ReadSelection(null, "transactions", List.of(new ReadSelection("Amount")))));
		var compiler = new ReadIntentCompiler(snapshot);
		var dynamic = compiler.compileOperationGraph(intent);
		var typed = SemanticOperationGraph.create(SemanticOperationCompiler
				.compile(new SemanticRequestResolver(snapshot).resolve(compiler.compile(intent))));
		assertEquals(SemanticOperationGraphFingerprint.create(typed),
				SemanticOperationGraphFingerprint.create(dynamic));
		assertEquals(snapshot.contractFingerprint(), compiler.contractFingerprint());
		assertEquals(4, dynamic.nodes().size());
		assertEquals(TRANSACTION, dynamic.getNode(3).entityId());
		assertEquals(AMOUNT, dynamic.getNode(3).fields().get(0));
	}

	@Test
	void dynamicLogicalTraversalPreservesAuthorizationAtEveryHop() {
		var model = buildModel();
		var snapshot = new SemanticContractSnapshot(model.freeze());
		var intent = new ReadIntent("Customer",
				List.of(new ReadSelection(null, "transactions", List.of(new ReadSelection("Amount")))));
		var graph = new SemanticRequestResolver(snapshot).resolve(new ReadIntentCompiler(snapshot).compile(intent));
		var authorized = new SemanticAuthorizer(new DenyContractPolicy()).authorize(graph);
		assertEquals(2, authorized.nodes().size());
		assertEquals(CUSTOMER, authorized.nodes().get(0).entityId());
		assertEquals(CUSTOMER_REL, authorized.nodes().get(1).entityId());
		assertTrue(authorized.nodes().stream().noneMatch(x -> x.entityId().equals(CONTRACT)));
		assertTrue(authorized.nodes().stream().noneMatch(x -> x.entityId().equals(TRANSACTION)));
	}

	private static SemanticModel buildModel() {
		return new SemanticModelBuilder()
				.entity(CUSTOMER, "Customer",
						e -> e.identity(CUSTOMER_ID, "Id").relationship(CUSTOMER_RELATIONSHIPS, "relationships",
								CUSTOMER_REL, RelationshipCardinality.MANY))
				.entity(CUSTOMER_REL, "CustomerRelationship",
						e -> e.identity(new FieldId(3), "Id").relationship(RELATIONSHIP_CONTRACT, "contract", CONTRACT,
								RelationshipCardinality.ONE))
				.entity(CONTRACT, "Contract",
						e -> e.identity(new FieldId(4), "Id").relationship(CONTRACT_TRANSACTIONS, "transactions",
								TRANSACTION, RelationshipCardinality.MANY))
				.entity(TRANSACTION, "Transaction",
						e -> e.identity(new FieldId(5), "Id").field(AMOUNT, "Amount", BigDecimal.class))
				.traversal(CUSTOMER, "transactions", CUSTOMER_RELATIONSHIPS, RELATIONSHIP_CONTRACT,
						CONTRACT_TRANSACTIONS)
				.build();
	}

	private static final class DenyContractPolicy extends AllowAllSemanticAuthorizationPolicy {
		@Override
		public boolean canAccessEntity(EntityId id) {
			return !id.equals(CONTRACT);
		}
	}
}
