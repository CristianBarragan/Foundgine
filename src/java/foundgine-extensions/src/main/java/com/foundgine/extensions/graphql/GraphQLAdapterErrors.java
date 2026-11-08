package com.foundgine.extensions.graphql;

import java.util.Locale;

/** Converts adapter failures into stable GraphQL-facing error categories. Port of {@code GraphQLAdapterErrors}. */
public final class GraphQLAdapterErrors {
    private GraphQLAdapterErrors() { }

    public static GraphQLAdapterError fromException(Exception exception) {
        // A missing/invalid security context is a distinct failure class from a malformed GraphQL request: it is never
        // the caller's request that is wrong, so it must not be reported as BAD_USER_INPUT or a validation failure,
        // both of which default to category BAD_REQUEST. (C# UnauthorizedAccessException == Java SecurityException.)
        if (exception instanceof SecurityException)
            return new GraphQLAdapterError(GraphQLAdapterErrorCode.UNAUTHENTICATED, exception.getMessage(), null,
                    GraphQLAdapterErrorCode.UNAUTHENTICATED);

        var message = exception.getMessage() == null ? "" : exception.getMessage();
        var lower = message.toLowerCase(Locale.ROOT);
        String code;
        if (lower.contains("variable") || lower.contains("expects") || lower.contains("cannot be null"))
            code = GraphQLAdapterErrorCode.BAD_USER_INPUT;
        else if (lower.contains("field") || lower.contains("argument") || lower.contains("fragment")
                || lower.contains("operation"))
            code = GraphQLAdapterErrorCode.GRAPHQL_VALIDATION_FAILED;
        else
            code = GraphQLAdapterErrorCode.ADAPTER_ERROR;
        return new GraphQLAdapterError(code, message);
    }
}
