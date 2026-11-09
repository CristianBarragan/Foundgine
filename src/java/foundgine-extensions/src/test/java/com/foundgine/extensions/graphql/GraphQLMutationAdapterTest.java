package com.foundgine.extensions.graphql;

import static com.foundgine.extensions.graphql.MutationAdapterFixtures.ACCOUNT;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import java.util.List;
import org.junit.jupiter.api.Test;

/** Port of the C# {@code MutationAdapterTests}. */
class GraphQLMutationAdapterTest {
    @Test
    void create_mutation_translates_to_mutation_intent_and_return_fields() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation {
                  createCustomer(input: { name: "Ada" }) { id name }
                }
                """);

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(MutationKind.CREATE, mutation.kind());
        assertEquals(1, mutation.fields().size());
        assertEquals("Ada", mutation.fields().get(0).value());
        assertEquals(List.of(new FieldId(1), new FieldId(2)), mutation.returnFields());
    }

    @Test
    void update_requires_where_and_translates_filter() {
        var intent = MutationAdapterFixtures.customer().adapter().adapt("""
                mutation {
                  updateCustomer(input: { name: "Grace" }, where: { id: { eq: 1 } }) { id name }
                }
                """);

        var mutation = assertInstanceOf(MutationIntent.class, intent.mutation());
        assertEquals(MutationKind.UPDATE, mutation.kind());
        assertNotNull(mutation.filter());
    }

    @Test
    void nested_create_mutation_becomes_nested_mutation_intent() {
        var intent = MutationAdapterFixtures.customerWithAccounts().adapter().adapt("""
                mutation {
                  createCustomer(input: { name: "Ada", accounts: [{ name: "Checking" }] }) { id name }
                }
                """);

        assertEquals(1, intent.children().size());
        var child = assertInstanceOf(MutationIntent.class, intent.children().get(0).mutation().mutation());
        assertEquals(ACCOUNT, child.entity());
        assertEquals(1, child.fields().size());
        assertEquals("Checking", child.fields().get(0).value());
    }
}
