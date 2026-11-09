package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import java.util.LinkedHashMap;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Port of the C# {@code MutationVariableTests}. */
class GraphQLMutationVariableTest {
    private static Map<String, Object> map(Object... keyValues) {
        var map = new LinkedHashMap<String, Object>();
        for (int i = 0; i < keyValues.length; i += 2) map.put((String) keyValues[i], keyValues[i + 1]);
        return map;
    }

    @Test
    void object_variable_produces_the_same_mutation_values_as_inline_input() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var inline = adapter.adapt("""
                mutation {
                  createCustomer(input: { name: "Ada" }) { id name }
                }
                """);

        var variable = adapter.adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id name }
                }
                """, map("input", map("name", "Ada")));

        var inlineMutation = assertInstanceOf(MutationIntent.class, inline.mutation());
        var variableMutation = assertInstanceOf(MutationIntent.class, variable.mutation());
        assertEquals(1, inlineMutation.fields().size());
        assertEquals(1, variableMutation.fields().size());
        assertEquals(inlineMutation.fields().get(0).value(), variableMutation.fields().get(0).value());
        assertEquals(inlineMutation.returnFields(), variableMutation.returnFields());
    }

    @Test
    void scalar_variable_can_be_used_inside_input_and_where() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation UpdateCustomer($name: String!, $id: Long!) {
                  updateCustomer(input: { name: $name }, where: { id: { eq: $id } }) { id name }
                }
                """, map("name", "Grace", "id", 42L));

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(1, mutation.fields().size());
        assertEquals("Grace", mutation.fields().get(0).value());
        var filter = assertInstanceOf(SemanticFieldFilter.class, mutation.filter());
        assertEquals(42L, filter.value());
    }

    @Test
    void nested_object_variable_is_resolved_at_the_graphql_boundary() {
        var intent = MutationAdapterFixtures.customerWithSimpleAccounts().adapter().adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id name }
                }
                """, map("input", map(
                "name", "Ada",
                "accounts", new Object[] {map("name", "Checking")})));

        assertEquals(1, intent.children().size());
        var childMutation = assertInstanceOf(MutationIntent.class, intent.children().get(0).mutation().mutation());
        assertEquals(1, childMutation.fields().size());
        assertEquals("Checking", childMutation.fields().get(0).value());
    }

    @Test
    void missing_variable_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var exception = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id name }
                }
                """));

        assertTrue(exception.getMessage().contains("$input"), exception.getMessage());
    }

    @Test
    void variable_default_value_is_used_when_runtime_value_is_absent() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation CreateCustomer($name: String = "Ada") {
                  createCustomer(input: { name: $name }) { id name }
                }
                """);

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(1, mutation.fields().size());
        assertEquals("Ada", mutation.fields().get(0).value());
    }
}
