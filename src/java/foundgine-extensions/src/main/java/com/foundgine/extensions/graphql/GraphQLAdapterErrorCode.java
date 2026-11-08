package com.foundgine.extensions.graphql;

/** Stable GraphQL-facing error codes. */
public final class GraphQLAdapterErrorCode {
    private GraphQLAdapterErrorCode() { }

    public static final String BAD_USER_INPUT = "BAD_USER_INPUT";
    public static final String GRAPHQL_VALIDATION_FAILED = "GRAPHQL_VALIDATION_FAILED";
    public static final String ADAPTER_ERROR = "GRAPHQL_ADAPTER_ERROR";
    public static final String UNAUTHENTICATED = "UNAUTHENTICATED";
}
