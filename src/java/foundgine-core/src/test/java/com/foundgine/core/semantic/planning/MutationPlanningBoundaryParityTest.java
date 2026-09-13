package com.foundgine.core.semantic.planning;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.planning.mutation.*;
import org.junit.jupiter.api.Test;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class MutationPlanningBoundaryParityTest {
	private static MutationSchema schema() {
		var entity = new MutationEntitySchema(new EntityId(1), "Customer", Set.of(new ColumnId(1), new ColumnId(2)),
				Map.of(new FieldId(1), new ColumnId(1), new FieldId(2), new ColumnId(2)), new ColumnId(1));
		return new MutationSchema() {
			public MutationEntitySchema getEntity(EntityId id) {
				return entity;
			}

			public MutationRelationshipSchema getRelationship(RelationshipId id) {
				throw new NoSuchElementException(id.toString());
			}
		};
	}

	@Test
	void plannerConsumesNarrowMutationSchema() {
		var intent = new MutationIntent(new EntityId(1), MutationKind.CREATE,
				List.of(new MutationFieldValue(new ColumnId(2), "Ada")), null, List.of(new FieldId(1), new FieldId(2)));
		var plan = new MutationPlanner(schema()).plan(intent);
		assertEquals(new EntityId(1), plan.operations().getFirst().entity().id());
		assertEquals("Customer", plan.operations().getFirst().entity().name());
		assertEquals(List.of(new FieldId(1), new FieldId(2)), plan.operations().getFirst().returnFields());
	}

	@Test
	void plannerRejectsUnfilteredUpdate() {
		var intent = new MutationIntent(new EntityId(1), MutationKind.UPDATE,
				List.of(new MutationFieldValue(new ColumnId(2), "Ada")), null, List.of(new FieldId(1)));
		assertThrows(IllegalStateException.class, () -> new MutationPlanner(schema()).plan(intent));
	}

	@Test
	void plannerRejectsUnknownReturnField() {
		var intent = new MutationIntent(new EntityId(1), MutationKind.CREATE,
				List.of(new MutationFieldValue(new ColumnId(2), "Ada")), null, List.of(new FieldId(999)));
		assertThrows(IllegalStateException.class, () -> new MutationPlanner(schema()).plan(intent));
	}
}
