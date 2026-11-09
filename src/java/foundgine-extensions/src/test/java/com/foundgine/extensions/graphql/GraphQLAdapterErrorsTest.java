package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import org.junit.jupiter.api.Test;

/** Error classification (C# GraphQLErrorSemanticsTests, adapter-error part). */
class GraphQLAdapterErrorsTest {
    @Test
    void variable_problems_are_bad_user_input() {
        var e = GraphQLAdapterErrors.fromException(new IllegalStateException("GraphQL variable value at $name must be String"));
        assertEquals("BAD_USER_INPUT", e.code());
        assertEquals("BAD_REQUEST", e.category());
        assertEquals("BAD_USER_INPUT",
                GraphQLAdapterErrors.fromException(new IllegalStateException("value cannot be null")).code());
    }

    @Test
    void field_argument_fragment_operation_problems_are_validation_failures() {
        for (var m : List.of("Field 'missing' is not defined", "unknown argument", "Fragment X", "No operation found"))
            assertEquals("GRAPHQL_VALIDATION_FAILED", GraphQLAdapterErrors.fromException(new IllegalStateException(m)).code(), m);
    }

    @Test
    void anything_else_is_an_adapter_error() {
        assertEquals("GRAPHQL_ADAPTER_ERROR", GraphQLAdapterErrors.fromException(new IllegalStateException("boom")).code());
        assertEquals("GRAPHQL_ADAPTER_ERROR", GraphQLAdapterErrors.fromException(new IllegalStateException()).code());
    }

    @Test
    void missing_security_context_is_unauthenticated_not_bad_request() {
        var e = GraphQLAdapterErrors.fromException(new SecurityException("no variable context for field"));
        assertEquals("UNAUTHENTICATED", e.code());
        assertEquals("UNAUTHENTICATED", e.category());
    }

    @Test
    void result_helpers() {
        var ok = GraphQLAdapterResult.success("x");
        assertTrue(ok.succeeded());
        assertNull(ok.error());
        var bad = GraphQLAdapterResult.<String>failure(new GraphQLAdapterError("C", "m"));
        assertFalse(bad.isSuccess());
        assertNull(bad.data());
        assertEquals("C", bad.error().code());
    }
}
