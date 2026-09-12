package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import org.junit.jupiter.api.Test;
import java.util.List;
import static org.junit.jupiter.api.Assertions.*;

/** Port of Foundgine.Semantics.Tests.SemanticResolutionBoundaryTests. */
class SemanticResolutionBoundaryParityTest {
	private SemanticContractSnapshot customerContract() {
		return new SemanticContractSnapshot(new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class))
				.build().freeze());
	}

	@Test
	void emptySelectionSetIsRejected() {
		var ex = assertThrows(IllegalStateException.class, () -> new SemanticRequestResolver(customerContract())
				.resolve(new SemanticRequest(new EntityId(1), List.of(), null, null)));
		assertTrue(ex.getMessage().contains("at least one selection"));
	}

	@Test
	void repeatedRelationshipSelectionIsRejectedBeforeGraphConstruction() {
		var customer = new EntityId(1);
		var account = new EntityId(2);
		var rel = new RelationshipId(1);
		var model = new SemanticModelBuilder()
				.entity(customer, "Customer",
						e -> e.identity(new FieldId(1), "Id").relationship(rel, "Accounts", account,
								RelationshipCardinality.MANY))
				.entity(account, "Account", e -> e.identity(new FieldId(1), "Id")).build();
		var child = new SemanticSelection(new FieldId(1), null, List.of());
		var request = new SemanticRequest(customer, List.of(new SemanticSelection(null, rel, List.of(child)),
				new SemanticSelection(null, rel, List.of(child))), null, null);
		var ex = assertThrows(IllegalStateException.class,
				() -> new SemanticRequestResolver(new SemanticContractSnapshot(model.freeze())).resolve(request));
		assertTrue(ex.getMessage().contains("selected more than once"));
	}

	@Test
	void resolverDoesNotRequireProviderMetadata() {
		var customer = new EntityId(1);
		var name = new FieldId(2);
		var model = new SemanticModelBuilder()
				.entity(customer, "Customer", e -> e.identity(new FieldId(1), "Id").field(name, "Name", String.class))
				.build();
		var graph = new SemanticRequestResolver(new SemanticContractSnapshot(model.freeze())).resolve(
				new SemanticRequest(customer, List.of(new SemanticSelection(name, null, List.of())), null, null));
		assertEquals(1, graph.nodes().size());
		assertEquals(customer, graph.nodes().get(0).entityId());
		assertEquals(List.of(name), graph.nodes().get(0).fields());
	}
}
