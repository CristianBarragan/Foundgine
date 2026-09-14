package com.foundgine.core.abstractions;

/**
 * Common marker implemented by every stable 64-bit semantic identity type in
 * this package (EntityId, FieldId, ColumnId, ConnectionId, ModelId,
 * RelationshipId, AuthorizationId). Not present in the C# source as a shared
 * interface — the C# types are independent {@code readonly record
 * struct}s — but added here to allow generic handling where useful; it changes
 * no wire format or behavior.
 */
public interface SemanticId {
	/** The identity value, interpreted as an unsigned 64-bit integer. */
	long value();
}
