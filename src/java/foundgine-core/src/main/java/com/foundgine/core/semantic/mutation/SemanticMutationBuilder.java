package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import java.util.*;

/** Factory for constructing canonical semantic mutation operations. */
public final class SemanticMutationBuilder {
	private SemanticMutationBuilder() {
	}

	public static SemanticMutationOperation create(EntityId entity, List<SemanticMutationField> fields,
			List<FieldId> returnFields) {
		return operation(entity, SemanticMutationKind.CREATE, fields, null, List.of(), returnFields);
	}

	public static SemanticMutationOperation update(EntityId entity, List<SemanticMutationField> fields,
			SemanticFilterExpression filter, List<FieldId> returnFields) {
		return operation(entity, SemanticMutationKind.UPDATE, fields, filter, List.of(), returnFields);
	}

	public static SemanticMutationOperation delete(EntityId entity, SemanticFilterExpression filter,
			List<FieldId> returnFields) {
		return operation(entity, SemanticMutationKind.DELETE, List.of(), filter, List.of(), returnFields);
	}

	public static SemanticMutationOperation upsert(EntityId entity, List<SemanticMutationField> fields,
			List<FieldId> conflictFields, List<FieldId> returnFields) {
		return operation(entity, SemanticMutationKind.UPSERT, fields, null, conflictFields, returnFields);
	}

	private static SemanticMutationOperation operation(EntityId entity, SemanticMutationKind kind,
			List<SemanticMutationField> fields, SemanticFilterExpression filter, List<FieldId> conflicts,
			List<FieldId> returns) {
		var fs = fields == null ? List.<SemanticMutationField>of() : List.copyOf(fields);
		var effects = new ArrayList<SemanticMutationEffect>();
		var effectKind = switch (kind) {
		case CREATE -> SemanticMutationEffectKind.CREATE_ENTITY;
		case UPDATE -> SemanticMutationEffectKind.UPDATE_ENTITY;
		case DELETE -> SemanticMutationEffectKind.DELETE_ENTITY;
		case UPSERT -> SemanticMutationEffectKind.UPSERT_ENTITY;
		};
		effects.add(new SemanticMutationEffect(effectKind, entity));
		fs.forEach(
				f -> effects.add(new SemanticMutationEffect(SemanticMutationEffectKind.SET_FIELD, entity, f.field())));
		return new SemanticMutationOperation(entity, kind, fs, filter,
				conflicts == null ? List.of() : List.copyOf(conflicts),
				returns == null ? List.of() : List.copyOf(returns), effects, List.of());
	}
}
