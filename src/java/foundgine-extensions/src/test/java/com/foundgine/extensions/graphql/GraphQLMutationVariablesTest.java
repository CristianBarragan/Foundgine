package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Port of the C# {@code MutationVariablesTests}. */
class GraphQLMutationVariablesTest {
    private static Map<String, Object> map(Object... keyValues) {
        var map = new LinkedHashMap<String, Object>();
        for (int i = 0; i < keyValues.length; i += 2) map.put((String) keyValues[i], keyValues[i + 1]);
        return map;
    }

    @Test
    void create_mutation_accepts_runtime_input_variable() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id name }
                }
                """, map("input", map("name", "Ada")));

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(MutationKind.CREATE, mutation.kind());
        assertEquals(1, mutation.fields().size());
        assertEquals("Ada", mutation.fields().get(0).value());
        assertEquals(List.of(new FieldId(1), new FieldId(2)), mutation.returnFields());
    }

    @Test
    void update_mutation_accepts_variable_for_input_and_where() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation UpdateCustomer($input: CustomerInput!, $where: CustomerWhereInput!) {
                  updateCustomer(input: $input, where: $where) { id name }
                }
                """, map("input", map("name", "Grace"), "where", map("id", map("eq", 1L))));

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(MutationKind.UPDATE, mutation.kind());
        assertEquals(1, mutation.fields().size());
        assertEquals("Grace", mutation.fields().get(0).value());
        assertNotNull(mutation.filter());
    }

    @Test
    void nested_variable_input_is_translated_like_inline_input() {
        var intent = MutationAdapterFixtures.customerWithAccounts().adapter().adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id name }
                }
                """, map("input", map(
                "name", "Ada",
                "accounts", new Object[] {map("name", "Checking")})));

        assertEquals(1, intent.children().size());
        var child = assertInstanceOf(MutationIntent.class, intent.children().get(0).mutation().mutation());
        assertEquals(MutationAdapterFixtures.ACCOUNT, child.entity());
        assertEquals(1, child.fields().size());
        assertEquals("Checking", child.fields().get(0).value());
    }

    @Test
    void declared_variable_without_runtime_value_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id }
                }
                """, new LinkedHashMap<String, Object>()));

        assertTrue(ex.getMessage().contains("$input"), ex.getMessage());
    }

    @Test
    void variable_syntax_without_runtime_values_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id }
                }
                """));

        assertTrue(ex.getMessage().contains("runtime variable-value dictionary"), ex.getMessage());
    }
}
