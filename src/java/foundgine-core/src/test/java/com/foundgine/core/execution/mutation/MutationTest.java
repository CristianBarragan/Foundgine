package com.foundgine.core.execution.mutation;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.RelationshipId;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

class MutationTest {

	@Test
	void totalAffectedRowsSumsAllResults() {
		MutationBatchResult batch = new MutationBatchResult(
				List.of(new MutationResult(2), new MutationResult(3), new MutationResult(0)));

		assertEquals(5, batch.totalAffectedRows());
	}

	@Test
	void childrenAreGroupedByRelationship() {
		MutationMaterializedNode parent = new MutationMaterializedNode(0, EntityId.create("Order"), Map.of());
		RelationshipId lineItems = RelationshipId.create("Order", "LineItems");

		MutationMaterializedNode child = new MutationMaterializedNode(1, EntityId.create("LineItem"), Map.of());
		parent.getChildren(lineItems).add(child);

		assertEquals(1, parent.children().get(lineItems).size());
		assertTrue(parent.children().get(lineItems).contains(child));
	}

	@Test
	void emptyResultHasNoRoots() {
		assertTrue(MutationMaterializedResult.EMPTY.roots().isEmpty());
	}
}
