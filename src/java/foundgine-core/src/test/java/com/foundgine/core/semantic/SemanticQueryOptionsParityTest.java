package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** C# SemanticQueryOptions behavioral parity tests. */
class SemanticQueryOptionsParityTest {
	@Test
	void filterOrderLimitAndOffsetRemainProtocolNeutral() {
		var filter = new SemanticAndFilter(
				List.of(new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, "Alice"),
						new SemanticFieldFilter(new FieldId(1), SemanticFilterOperator.IN, List.of(1, 2))));
		var options = new SemanticQueryOptions(filter,
				List.of(new SemanticOrderTerm(new FieldId(2), SemanticSortDirection.DESC, List.of(), null)), 10, 20,
				null);

		assertInstanceOf(SemanticAndFilter.class, options.filter());
		assertEquals(SemanticSortDirection.DESC, options.effectiveOrder().get(0).direction());
		assertEquals(10, options.limit());
		assertEquals(20, options.offset());
	}
}
