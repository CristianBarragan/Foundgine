package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.*;

/** One semantic effect associated with a mutation operation. */
public record SemanticMutationEffect(SemanticMutationEffectKind kind, EntityId entity, FieldId field, RelationshipId relationship) {
    public SemanticMutationEffect { if (kind == null || entity == null) throw new NullPointerException(); }
    public SemanticMutationEffect(SemanticMutationEffectKind kind, EntityId entity) { this(kind, entity, null, null); }
    public SemanticMutationEffect(SemanticMutationEffectKind kind, EntityId entity, FieldId field) { this(kind, entity, field, null); }
}
