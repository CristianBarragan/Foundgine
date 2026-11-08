package com.foundgine.extensions.graphql;

import java.util.List;

/**
 * Structured GraphQL-facing adapter error. The core Foundgine pipeline stays exception/transport agnostic; hosts can map
 * this error to GraphQL errors. Port of {@code GraphQLAdapterError}.
 *
 * @param path response path, or null
 * @param category defaults to {@code BAD_REQUEST}
 */
public record GraphQLAdapterError(String code, String message, List<Object> path, String category) {
    public GraphQLAdapterError(String code, String message) {
        this(code, message, null, "BAD_REQUEST");
    }
}
