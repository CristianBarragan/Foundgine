package com.foundgine.extensions.graphql;

import static com.foundgine.extensions.graphql.MutationAdapterFixtures.containsIgnoreCase;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import java.util.LinkedHashMap;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Port of the C# {@code MutationOperationSelectionTests}. */
class GraphQLMutationOperationSelectionTest {
    private static MutationKind kindOf(com.foundgine.core.semantic.planning.mutation.NestedMutationIntent intent) {
        return assertInstanceOf(MutationIntent.class, intent.mutation()).kind();
    }

    @Test
    void named_operation_is_selected_from_multi_operation_document() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var input = new LinkedHashMap<String, Object>();
        input.put("name", "Ada");
        var where = new LinkedHashMap<String, Object>();
        where.put("id", 7);
        var variables = new LinkedHashMap<String, Object>();
        variables.put("input", input);
        variables.put("where", where);

        var intent = adapter.adapt("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id name }
                }

                mutation UpdateCustomer($input: CustomerInput!, $where: CustomerWhereInput!) {
                  updateCustomer(input: $input, where: $where) { id name }
                }
                """, "UpdateCustomer", variables);

        assertEquals(MutationKind.UPDATE, kindOf(intent));
    }

    @Test
    void multiple_operations_require_an_operation_name() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                mutation DeleteCustomer { deleteCustomer(where: { id: { eq: 7 } }) { id } }
                """));

        assertTrue(containsIgnoreCase(ex.getMessage(), "operation name"), ex.getMessage());
    }

    @Test
    void unknown_operation_name_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                mutation UpdateCustomer { updateCustomer(input: { name: "Grace" }, where: { id: { eq: 7 } }) { id } }
                """, "DeleteCustomer"));

        assertTrue(ex.getMessage().contains("DeleteCustomer"), ex.getMessage());
        assertTrue(containsIgnoreCase(ex.getMessage(), "not found"), ex.getMessage());
    }

    @Test
    void selected_query_operation_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                query CustomerQuery { customer { id name } }
                mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                """, "CustomerQuery"));

        assertTrue(containsIgnoreCase(ex.getMessage(), "mutation operation"), ex.getMessage());
    }

    @Test
    void single_anonymous_mutation_still_works_without_operation_name() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation { createCustomer(input: { name: "Ada" }) { id name } }
                """);

        assertEquals(MutationKind.CREATE, kindOf(intent));
    }

    @Test
    void operation_name_can_be_selected_without_variables() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation CreateCustomer { createCustomer(input: { name: "Ada" }) { id } }
                mutation UpdateCustomer { updateCustomer(input: { name: "Grace" }, where: { id: { eq: 7 } }) { id } }
                """, "CreateCustomer");

        assertEquals(MutationKind.CREATE, kindOf(intent));
        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(1, mutation.fields().size());
        assertEquals("Ada", mutation.fields().get(0).value());
    }
}
