package com.foundgine.extensions.graphql;

import static com.foundgine.extensions.graphql.MutationAdapterFixtures.containsIgnoreCase;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import java.util.List;
import org.junit.jupiter.api.Test;

/** Port of the C# {@code MutationFragmentTests}. */
class GraphQLMutationFragmentTest {
    @Test
    void named_fragment_is_expanded_into_mutation_result_fields() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "Ada" }) {
                    ...CustomerFields
                  }
                }

                fragment CustomerFields on Customer {
                  id
                  name
                }
                """);

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(List.of(new FieldId(1), new FieldId(2)), mutation.returnFields());
    }

    @Test
    void fragment_fields_are_deduplicated() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "Ada" }) {
                    id
                    ...CustomerFields
                  }
                }

                fragment CustomerFields on Customer {
                  id
                  name
                }
                """);

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(List.of(new FieldId(1), new FieldId(2)), mutation.returnFields());
    }

    @Test
    void fragment_cycle_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "Ada" }) { ...A }
                }
                fragment A on Customer { ...B }
                fragment B on Customer { ...A }
                """));

        assertTrue(containsIgnoreCase(ex.getMessage(), "cycle"), ex.getMessage());
    }

    @Test
    void missing_fragment_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "Ada" }) { ...MissingFields }
                }
                """));

        assertTrue(ex.getMessage().contains("MissingFields"), ex.getMessage());
        assertTrue(containsIgnoreCase(ex.getMessage(), "not found"), ex.getMessage());
    }

    @Test
    void fragment_on_different_type_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "Ada" }) { ...AccountFields }
                }
                fragment AccountFields on Account { id }
                """));

        assertTrue(containsIgnoreCase(ex.getMessage(), "cannot be applied"), ex.getMessage());
    }

    @Test
    void inline_fragment_on_matching_type_is_supported() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "Ada" }) {
                    ... on Customer { id name }
                  }
                }
                """);

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(List.of(new FieldId(1), new FieldId(2)), mutation.returnFields());
    }
}
