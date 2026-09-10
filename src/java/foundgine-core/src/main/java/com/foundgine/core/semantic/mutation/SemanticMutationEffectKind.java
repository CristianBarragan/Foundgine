package com.foundgine.core.semantic.mutation;

/** Semantic effects produced by a mutation. */
public enum SemanticMutationEffectKind {
    CREATE_ENTITY, UPDATE_ENTITY, UPSERT_ENTITY, DELETE_ENTITY,
    SET_FIELD, CONNECT_RELATIONSHIP, DISCONNECT_RELATIONSHIP
}
