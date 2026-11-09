package com.foundgine.extensions.graphql;

/**
 * Port of C# {@code GraphQLMutationBatchItem}: one entry of a batched GraphQL mutation document. {@code resultKey} is
 * the field's GraphQL alias (or, for the single-unaliased-field convenience case, its field name); use it both to key
 * the response object per item and to line up each item's planned operations with the planner's batch overload.
 */
public record GraphQLMutationBatchItem(String resultKey, GraphQLMutationAdaptation adaptation) {}
