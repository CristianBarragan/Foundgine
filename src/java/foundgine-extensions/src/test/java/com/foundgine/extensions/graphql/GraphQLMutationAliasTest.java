package com.foundgine.extensions.graphql;

import static com.foundgine.extensions.graphql.MutationAdapterFixtures.containsIgnoreCase;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.mutation.MutationMaterializedNode;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.extensions.graphql.GraphQLMutationResultShape.GraphQLMutationResultField;
import java.util.LinkedHashMap;
import java.util.List;
import org.junit.jupiter.api.Test;

/** Port of the C# {@code MutationAliasTests}. */
class GraphQLMutationAliasTest {
    private static final String CREATE_WITH_ALIASES = """
            mutation CreateCustomer {
              createCustomer(input: { name: "Ada" }) {
                customerId: id
                displayName: name
              }
            }
            """;

    @Test
    void alias_is_kept_out_of_mutation_intent_and_preserved_in_result_shape() {
        var adapted = MutationAdapterFixtures.customer().adapter().adaptWithResultShape(CREATE_WITH_ALIASES);

        var mutation = assertInstanceOf(MutationIntent.class, adapted.intent().mutation());
        assertEquals(List.of(new FieldId(1), new FieldId(2)), mutation.returnFields());
        assertEquals(
                List.of(
                        new GraphQLMutationResultField(new FieldId(1), "customerId"),
                        new GraphQLMutationResultField(new FieldId(2), "displayName")),
                adapted.resultShape().fields());
    }

    @Test
    void alias_can_be_applied_to_materialized_result() {
        var adapted = MutationAdapterFixtures.customer().adapter().adaptWithResultShape(CREATE_WITH_ALIASES);

        var values = new LinkedHashMap<FieldId, Object>();
        values.put(new FieldId(1), 42L);
        values.put(new FieldId(2), "Ada");
        var materialized = new MutationMaterializedNode(0, new EntityId(1), values);

        var shaped = GraphQLMutationResultShaper.shape(materialized, adapted.resultShape());

        assertEquals(42L, shaped.get("customerId"));
        assertEquals("Ada", shaped.get("displayName"));
    }

    @Test
    void same_response_alias_is_rejected() {
        var adapter = MutationAdapterFixtures.customer().adapter();

        var ex = assertThrows(IllegalStateException.class, () -> adapter.adaptWithResultShape("""
                mutation CreateCustomer {
                  createCustomer(input: { name: "Ada" }) {
                    value: id
                    value: name
                  }
                }
                """));

        assertTrue(containsIgnoreCase(ex.getMessage(), "duplicate"), ex.getMessage());
    }
}
