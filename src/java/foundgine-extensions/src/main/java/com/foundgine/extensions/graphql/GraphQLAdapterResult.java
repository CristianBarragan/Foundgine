package com.foundgine.extensions.graphql;

import java.util.List;

/** Data-or-errors result of the non-throwing {@code tryAdapt} APIs. Port of {@code GraphQLAdapterResult<T>}. */
public record GraphQLAdapterResult<T>(T data, List<GraphQLAdapterError> errors) {
    public GraphQLAdapterResult {
        errors = List.copyOf(errors);
    }

    public boolean succeeded() {
        return errors.isEmpty();
    }

    public boolean isSuccess() {
        return succeeded();
    }

    /** First error, or null. */
    public GraphQLAdapterError error() {
        return errors.isEmpty() ? null : errors.get(0);
    }

    public static <T> GraphQLAdapterResult<T> success(T data) {
        return new GraphQLAdapterResult<>(data, List.of());
    }

    public static <T> GraphQLAdapterResult<T> failure(GraphQLAdapterError error) {
        return new GraphQLAdapterResult<>(null, List.of(error));
    }
}
