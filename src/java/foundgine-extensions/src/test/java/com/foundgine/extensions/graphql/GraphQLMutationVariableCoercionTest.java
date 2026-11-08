package com.foundgine.extensions.graphql;

import static com.foundgine.extensions.graphql.MutationAdapterFixtures.containsIgnoreCase;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.UUID;
import org.junit.jupiter.api.Test;

/** Port of the C# {@code MutationVariableCoercionTests}. */
class GraphQLMutationVariableCoercionTest {
    private static Map<String, Object> map(Object... keyValues) {
        var map = new LinkedHashMap<String, Object>();
        for (int i = 0; i < keyValues.length; i += 2) map.put((String) keyValues[i], keyValues[i + 1]);
        return map;
    }

    private static final String CREATE_WITH_INPUT = """
            mutation CreateCustomer($input: CustomerInput!) {
              createCustomer(input: $input) { id name }
            }
            """;

    @Test
    void wrong_scalar_variable_type_is_rejected_before_translation() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt(CREATE_WITH_INPUT, map("input", "Ada")));

        assertTrue(containsIgnoreCase(ex.getMessage(), "input"), ex.getMessage());
    }

    @Test
    void non_null_variable_cannot_be_null() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id }
                }
                """, map("input", null)));

        assertTrue(containsIgnoreCase(ex.getMessage(), "cannot be null"), ex.getMessage());
    }

    @Test
    void list_variable_requires_a_runtime_list() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomers($inputs: [CustomerInput!]!) {
                  createCustomer(input: $inputs) { id }
                }
                """, map("inputs", map("name", "Ada"))));

        assertTrue(containsIgnoreCase(ex.getMessage(), "list"), ex.getMessage());
    }

    @Test
    void variable_default_value_is_used_when_runtime_value_is_omitted() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation CreateCustomer($input: CustomerInput! = { name: "Ada" }) {
                  createCustomer(input: $input) { id name }
                }
                """);

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(1, mutation.fields().size());
        assertEquals("Ada", mutation.fields().get(0).value());
    }

    @Test
    void extra_runtime_variables_are_ignored_like_graphql_variable_coercion() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt(
                CREATE_WITH_INPUT, map("input", map("name", "Ada"), "unused", 123));

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(1, mutation.fields().size());
        assertEquals("Ada", mutation.fields().get(0).value());
    }

    @Test
    void input_object_field_type_is_checked_against_semantic_field_type() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class,
                () -> adapter.adapt(CREATE_WITH_INPUT, map("input", map("name", 123))));

        assertTrue(ex.getMessage().contains("expects 'String'"), ex.getMessage());
    }

    @Test
    void guid_string_is_coerced_to_a_clr_guid_before_sql_execution() {
        var adapter = MutationAdapterFixtures.guidCustomer().adapter();
        var expected = UUID.fromString("550e8400-e29b-41d4-a716-446655440000");

        var intent = adapter.adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id customerKey }
                }
                """, map("input", map("customerKey", expected.toString())));

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(1, mutation.fields().size());
        var value = mutation.fields().get(0).value();
        assertEquals(expected, value);
        assertInstanceOf(UUID.class, value);
    }

    @Test
    void nullable_guid_field_accepts_null() {
        var adapter = MutationAdapterFixtures.guidCustomer().adapter();

        var intent = adapter.adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id customerKey }
                }
                """, map("input", map("customerKey", null)));

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(1, mutation.fields().size());
        assertNull(mutation.fields().get(0).value());
    }

    @Test
    void non_nullable_semantic_field_cannot_receive_null() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class,
                () -> adapter.adapt(CREATE_WITH_INPUT, map("input", map("id", null))));

        assertTrue(containsIgnoreCase(ex.getMessage(), "cannot be null"), ex.getMessage());
    }
}
