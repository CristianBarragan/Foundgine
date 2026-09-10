package com.foundgine.core.abstractions;

/** Port of {@code Foundgine.Core.Abstractions.MutationRelationshipSchema}. */
public record MutationRelationshipSchema(
        RelationshipId id,
        EntityId source,
        EntityId target,
        String name,
        ColumnId sourceColumn,
        ColumnId targetColumn) {
}
