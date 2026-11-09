package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/**
 * Acceptance-level parity cases for the C# GraphQLMutationAcceptanceTests and the 9d-2e boundary.
 * These tests deliberately stop at provider-neutral mutation intents and result-shape contracts.
 */
class GraphQLMutationAcceptanceParityTest {
    @Test
    void complete_nested_mutation_preserves_semantic_intent_and_graphql_response_aliases() {
        var fixture = MutationAdapterFixtures.customerWithAccounts();
        var adapted = fixture.adapter().adaptWithResultShape("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) {
                    customerId: id
                    displayName: name
                    accounts {
                      accountId: id
                      accountName: name
                    }
                  }
                }
                """, Map.of("input", Map.of(
                "name", "Ada",
                "accounts", List.of(Map.of("name", "Checking")))), null);

        var root = (MutationIntent) adapted.intent().mutation();
        assertEquals("Ada", root.fields().get(0).value());
        assertEquals(List.of(new FieldId(1), new FieldId(2)), root.returnFields());
        assertEquals(1, adapted.intent().children().size());

        var relationship = adapted.resultShape().relationships().get(0);
        assertEquals("accounts", relationship.responseName());
        assertEquals(List.of("accountId", "accountName"),
                relationship.shape().fields().stream().map(GraphQLMutationResultShape.GraphQLMutationResultField::responseName).toList());
        assertEquals(List.of("customerId", "displayName"),
                adapted.resultShape().fields().stream().map(GraphQLMutationResultShape.GraphQLMutationResultField::responseName).toList());
    }

    @Test
    void generated_identity_is_selected_for_output_but_never_required_as_mutation_input() {
        var fixture = MutationAdapterFixtures.customer();
        var adapted = fixture.adapter().adaptWithResultShape("""
                mutation CreateCustomer($input: CustomerInput!) {
                  createCustomer(input: $input) { id name }
                }
                """, Map.of("input", Map.of("name", "Ada")), null);

        var mutation = (MutationIntent) adapted.intent().mutation();
        assertEquals(1, mutation.fields().size());
        assertEquals("Ada", mutation.fields().get(0).value());
        assertEquals(List.of(new FieldId(1), new FieldId(2)), mutation.returnFields());
    }

    @Test
    void unsupported_mutation_directive_is_rejected_at_adapter_boundary() {
        var fixture = MutationAdapterFixtures.customer();
        var error = assertThrows(IllegalStateException.class, () -> fixture.adapter().adapt("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "Ada" }) @skip(if: false) { id }
                }
                """));
        assertTrue(MutationAdapterFixtures.containsIgnoreCase(error.getMessage(), "directives"));
    }

    @Test
    void client_supplied_tenant_or_subject_fields_are_not_silently_forwarded() {
        var fixture = MutationAdapterFixtures.customer();
        var error = assertThrows(RuntimeException.class, () -> fixture.adapter().adapt("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "attacker", tenantId: "other-tenant", subject: "other-user" }) { id }
                }
                """));
        assertFalse(error.getMessage() == null || error.getMessage().isBlank());
    }
}
