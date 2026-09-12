package com.foundgine.core.security.penetration;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.authorization.*;
import com.foundgine.core.semantic.mutation.*;
import com.foundgine.core.semantic.planning.mutation.*;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;

import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Hostile mutation-plan parity tests: unauthorized fields, filters, and batch
 * operations fail closed.
 */
class MutationAuthorizationPenetrationParityTest {

	@Test
	void unauthorizedWriteFieldIsRejected() {
		var entity = entity(1, "Account", Map.entry(1, 11), Map.entry(2, 12));
		var schema = new TestSchema(entity);
		var plan = new MutationPlan(List.of(new MutationOperation(entity, MutationKind.UPDATE,
				List.of(new MutationFieldValue(new ColumnId(12), "attacker")), null)));

		var exception = assertThrows(SemanticAuthorizationException.class,
				() -> new MutationAuthorizer(schema, new AllowOnlyFieldPolicy(new FieldId(1))).authorize(plan));
		assertTrue(exception.getMessage().toLowerCase(Locale.ROOT).contains("field"));
	}

	@Test
	void unauthorizedReturnFieldIsRejected() {
		var entity = entity(1, "Account", Map.entry(1, 11), Map.entry(2, 12));
		var schema = new TestSchema(entity);
		var plan = new MutationPlan(List.of(
				new MutationOperation(entity, MutationKind.UPDATE, List.of(), null, null, List.of(new FieldId(2)))));

		assertThrows(SemanticAuthorizationException.class,
				() -> new MutationAuthorizer(schema, new AllowOnlyFieldPolicy(new FieldId(1))).authorize(plan));
	}

	@Test
	void unauthorizedFilterFieldIsRejected() {
		var entity = entity(1, "Account", Map.entry(1, 11), Map.entry(2, 12));
		var schema = new TestSchema(entity);
		var filter = new SemanticFieldFilter(new FieldId(2), SemanticFilterOperator.EQ, "victim");
		var plan = new MutationPlan(List.of(new MutationOperation(entity, MutationKind.DELETE, List.of(), filter)));

		assertThrows(SemanticAuthorizationException.class,
				() -> new MutationAuthorizer(schema, new AllowOnlyFieldPolicy(new FieldId(1))).authorize(plan));
	}

	@Test
	void batchWithUnauthorizedOperationLastRejectsEntireBatch() {
		var entity = entity(1, "Account", Map.entry(1, 11), Map.entry(2, 12));
		var schema = new TestSchema(entity);
		var policy = new AllowOnlyFieldPolicy(new FieldId(1));
		var graph = new SemanticMutationOperationGraph(List.of(
				SemanticMutationBuilder.update(entity.id(), List.of(new SemanticMutationField(new FieldId(1), "ok")),
						null, null),
				SemanticMutationBuilder.update(entity.id(),
						List.of(new SemanticMutationField(new FieldId(2), "attacker")), null, null)));
		var semanticPlan = new SemanticMutationPlanner().plan(graph);

		var exception = assertThrows(SemanticAuthorizationException.class,
				() -> new MutationAuthorizer(schema, policy).authorize(semanticPlan));
		assertTrue(exception.getMessage().toLowerCase(Locale.ROOT).contains("field"));
	}

	@Test
	void batchWithUnauthorizedOperationFirstRejectsEntireBatch() {
		var entity = entity(1, "Account", Map.entry(1, 11), Map.entry(2, 12));
		var schema = new TestSchema(entity);
		var policy = new AllowOnlyFieldPolicy(new FieldId(1));
		var graph = new SemanticMutationOperationGraph(List.of(
				SemanticMutationBuilder.update(entity.id(),
						List.of(new SemanticMutationField(new FieldId(2), "attacker")), null, null),
				SemanticMutationBuilder.update(entity.id(), List.of(new SemanticMutationField(new FieldId(1), "ok")),
						null, null)));
		var semanticPlan = new SemanticMutationPlanner().plan(graph);

		assertThrows(SemanticAuthorizationException.class,
				() -> new MutationAuthorizer(schema, policy).authorize(semanticPlan));
	}

	@Test
	void batchAuthorizationDoesNotLeakPartiallyAuthorizedPlan() {
		var entity = entity(1, "Account", Map.entry(1, 11), Map.entry(2, 12));
		var schema = new TestSchema(entity);
		var policy = new AllowOnlyFieldPolicy(new FieldId(1));
		var graph = new SemanticMutationOperationGraph(List.of(
				SemanticMutationBuilder.update(entity.id(), List.of(new SemanticMutationField(new FieldId(1), "ok")),
						null, null),
				SemanticMutationBuilder.update(entity.id(),
						List.of(new SemanticMutationField(new FieldId(2), "attacker")), null, null)));
		var semanticPlan = new SemanticMutationPlanner().plan(graph);

		SemanticMutationPlan authorized = null;
		Throwable exception = null;
		try {
			authorized = new MutationAuthorizer(schema, policy).authorize(semanticPlan);
		} catch (Throwable e) {
			exception = e;
		}

		assertTrue(exception instanceof SemanticAuthorizationException);
		assertNull(authorized);
	}

	@SafeVarargs
	private static MutationEntitySchema entity(int id, String name, Map.Entry<Integer, Integer>... fields) {
		var map = new LinkedHashMap<FieldId, ColumnId>();
		for (var field : fields)
			map.put(new FieldId(field.getKey()), new ColumnId(field.getValue()));
		return new MutationEntitySchema(new EntityId(id), name, new LinkedHashSet<>(map.values()), map,
				map.values().iterator().next());
	}

	private static class AllowOnlyFieldPolicy extends AllowAllSemanticAuthorizationPolicy {
		private final FieldId allowed;

		AllowOnlyFieldPolicy(FieldId allowed) {
			this.allowed = allowed;
		}

		@Override
		public boolean canWriteEntity(EntityId entityId) {
			return true;
		}

		@Override
		public boolean canWriteField(EntityId entityId, FieldId fieldId) {
			return fieldId.equals(allowed);
		}

		@Override
		public boolean canAccessField(EntityId entityId, FieldId fieldId) {
			return fieldId.equals(allowed);
		}
	}

	private record TestSchema(MutationEntitySchema entity) implements MutationSchema {
		@Override
		public MutationEntitySchema getEntity(EntityId entityId) {
			if (entity.id().equals(entityId))
				return entity;
			throw new NoSuchElementException();
		}

		@Override
		public MutationRelationshipSchema getRelationship(RelationshipId relationshipId) {
			throw new NoSuchElementException();
		}
	}
}
