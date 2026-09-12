package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.query.SemanticOrderAggregate;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import com.foundgine.core.semantic.resolution.SemanticRequestResolver;
import org.junit.jupiter.api.Test;

import java.math.BigDecimal;
import java.util.List;
import java.util.NoSuchElementException;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of {@code Foundgine.Core.Semantic.Tests.SemanticSemanticContractTests}.
 */
class SemanticSemanticContractParityTest {

	@Test
	void builtModelIsSnapshotAfterBuilderChanges() {
		var builder = new SemanticModelBuilder().entity(new EntityId(1), "Customer",
				e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class));

		var first = builder.build();

		builder.entity(new EntityId(2), "Account", e -> e.identity(new FieldId(1), "Id"));

		assertEquals(1, first.entities().size());
		assertThrows(NoSuchElementException.class, () -> first.get(new EntityId(2)));
	}

	@Test
	void semanticFieldExposesProviderIndependentTypeAndCapabilities() {
		var field = new SemanticField(new FieldId(2), "Balance", BigDecimal.class, null,
				(byte) (SemanticFieldCapabilities.FILTERABLE | SemanticFieldCapabilities.SORTABLE), List.of(),
				List.of(), null);

		var scalar = assertInstanceOf(SemanticType.Scalar.class, field.effectiveSemanticType());
		assertEquals(SemanticScalarKind.DECIMAL, scalar.kind());
		assertTrue((field.capabilities() & SemanticFieldCapabilities.FILTERABLE) != 0);
		assertFalse((field.capabilities() & SemanticFieldCapabilities.AGGREGATABLE) != 0);
	}

	@Test
	void resolverCanonicalizesCountAndAddsCursorIdentityTieBreaker() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer",
						e -> e.identity(new FieldId(1), "Id").field(new FieldId(2), "Name", String.class)
								.relationship(new RelationshipId(10), "Orders", new EntityId(2),
										RelationshipCardinality.MANY))
				.entity(new EntityId(2), "Order",
						e -> e.identity(new FieldId(3), "Id").field(new FieldId(4), "Total", BigDecimal.class))
				.build();

		var options = new SemanticQueryOptions(null,
				List.of(new SemanticOrderTerm(new FieldId(999), SemanticSortDirection.DESC,
						List.of(new RelationshipId(10)), SemanticOrderAggregate.COUNT)),
				10, null, "cursor");
		var request = new SemanticRequest(new EntityId(1), List.of(new SemanticSelection(new FieldId(1), null, List.of())),
				options, null);

		var resolved = new SemanticRequestResolver(model.freeze().createSnapshot()).resolve(request);
		var order = resolved.options().effectiveOrder();

		assertEquals(new FieldId(3), order.get(0).field());
		assertEquals(SemanticOrderAggregate.COUNT, order.get(0).aggregate());
		assertEquals(new FieldId(1), order.get(1).field());
		assertTrue(order.get(1).effectivePath().isEmpty());
	}

	@Test
	void resolverRejectsNegativeQueryControlsBeforePlanning() {
		var model = new SemanticModelBuilder()
				.entity(new EntityId(1), "Customer", e -> e.identity(new FieldId(1), "Id")).build();

		var options = new SemanticQueryOptions(null, List.of(), -1, null, null);
		var request = new SemanticRequest(new EntityId(1), List.of(new SemanticSelection(new FieldId(1), null, List.of())),
				options, null);

		var ex = assertThrows(IllegalStateException.class,
				() -> new SemanticRequestResolver(model.freeze().createSnapshot()).resolve(request));
		assertTrue(ex.getMessage().toLowerCase(java.util.Locale.ROOT).contains("limit"));
	}
}