package com.foundgine.core.semantic.mutation;

/** Domain-level mutation intent; deliberately independent of storage verbs. */
public enum SemanticMutationKind { CREATE, UPDATE, DELETE, UPSERT }
