package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Locale;

import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.Core.Semantic.Tests.SemanticGraphValidationTests}
 * (declared alongside {@code SemanticSemanticContractTests} in the C# source
 * file).
 */
class SemanticGraphValidationParityTest {

	@Test
	void resolverProvesRelationshipTargetConsistency() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").relationship(new RelationshipId(10), "Orders",
								new EntityId(2), RelationshipCardinality.MANY))
				.entity(new EntityId(2), "Order", e -> e.identity(new FieldId(3), "Id")).build();

		var graph = new SemanticGraph();
		var root = graph.addRoot(new EntityId(1), List.of(new FieldId(1)));
		graph.add(new EntityId(999), new RelationshipId(10), root, List.of(new FieldId(1)));

		var ex = assertThrows(IllegalStateException.class, () -> SemanticGraphValidator.validate(graph, model));
		assertTrue(ex.getMessage().toLowerCase(Locale.ROOT).contains("targets entity"));
	}
}