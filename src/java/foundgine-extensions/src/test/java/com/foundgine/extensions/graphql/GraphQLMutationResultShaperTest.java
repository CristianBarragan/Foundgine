package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.mutation.MutationBatchResult;
import com.foundgine.core.execution.mutation.MutationMaterializedResult;
import com.foundgine.core.execution.mutation.MutationResult;
import com.foundgine.core.execution.mutation.MutationResultMaterializer;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import com.foundgine.core.semantic.planning.mutation.NestedMutationChild;
import com.foundgine.core.semantic.planning.mutation.NestedMutationIntent;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

import com.foundgine.extensions.graphql.GraphQLMutationResultShape.GraphQLMutationResultField;
import com.foundgine.extensions.graphql.GraphQLMutationResultShape.GraphQLMutationResultRelationship;

/**
 * Port of the C# {@code NestedMutationResultShapingTests} shaping half. The intent/shape are built by hand here; the
 * adapter-driven variants arrive with the mutation adapter (9d-2).
 */
class GraphQLMutationResultShaperTest {
    private static final EntityId CUSTOMER = new EntityId(1);
    private static final EntityId ACCOUNT = new EntityId(2);
    private static final RelationshipId ACCOUNTS = new RelationshipId(10);
    private static final RelationshipId PRIMARY = new RelationshipId(11);

    private static SemanticModel model(RelationshipId relationship, String name, RelationshipCardinality cardinality) {
        return new SemanticModelBuilder()
                .entity(CUSTOMER, "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class)
                        .relationship(relationship, name, ACCOUNT, cardinality))
                .entity(ACCOUNT, "Account", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "CustomerId", Long.class)
                        .field(new FieldId(3), "Name", String.class))
                .build();
    }

    private static NestedMutationIntent nested(RelationshipId relationship, int children) {
        var childIntent = new NestedMutationIntent(
                new MutationIntent(ACCOUNT, MutationKind.CREATE, List.of(), null, List.of(new FieldId(1), new FieldId(3))), List.of());
        var kids = new ArrayList<NestedMutationChild>();
        for (int i = 0; i < children; i++) kids.add(new NestedMutationChild(relationship, childIntent));
        return new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.CREATE, List.of(), null, List.of(new FieldId(1), new FieldId(2))), kids);
    }

    private static GraphQLMutationResultShape accountShape() {
        return new GraphQLMutationResultShape(
                List.of(new GraphQLMutationResultField(new FieldId(1), "id", "accountId"),
                        new GraphQLMutationResultField(new FieldId(3), "name", "accountName")),
                List.of());
    }

    private static GraphQLMutationResultShape customerShape(RelationshipId relationship, boolean collection) {
        return new GraphQLMutationResultShape(
                List.of(new GraphQLMutationResultField(new FieldId(1), "id", "customerId"),
                        new GraphQLMutationResultField(new FieldId(2), "name", "displayName")),
                List.of(new GraphQLMutationResultRelationship(
                        relationship, collection ? "accounts" : "primary", collection ? "accounts" : "primary",
                        accountShape(), collection)));
    }

    private static Map<FieldId, Object> values(Object... kv) {
        var map = new LinkedHashMap<FieldId, Object>();
        for (int i = 0; i < kv.length; i += 2) map.put((FieldId) kv[i], kv[i + 1]);
        return map;
    }

    @Test
    void nested_relationship_result_is_shaped_with_aliases() {
        var model = model(ACCOUNTS, "Accounts", RelationshipCardinality.MANY);
        var intent = nested(ACCOUNTS, 1);
        var materialized = new MutationResultMaterializer(model).materialize(
                intent,
                new MutationBatchResult(List.of(
                        new MutationResult(1, values(new FieldId(1), 42L, new FieldId(2), "Ada")),
                        new MutationResult(1, values(new FieldId(1), 7L, new FieldId(3), "Checking")))));

        var shaped = GraphQLMutationResultShaper.shape(materialized.roots().get(0), customerShape(ACCOUNTS, true));

        assertEquals(42L, shaped.get("customerId"));
        assertEquals("Ada", shaped.get("displayName"));
        @SuppressWarnings("unchecked")
        var accounts = (List<Object>) shaped.get("accounts");
        assertEquals(1, accounts.size());
        @SuppressWarnings("unchecked")
        var account = (Map<String, Object>) accounts.get(0);
        assertEquals(7L, account.get("accountId"));
        assertEquals("Checking", account.get("accountName"));
    }

    @Test
    void response_keys_keep_field_then_relationship_declaration_order() {
        var model = model(ACCOUNTS, "Accounts", RelationshipCardinality.MANY);
        var materialized = new MutationResultMaterializer(model).materialize(
                nested(ACCOUNTS, 1),
                new MutationBatchResult(List.of(
                        new MutationResult(1, values(new FieldId(1), 1L, new FieldId(2), "A")),
                        new MutationResult(1, values(new FieldId(1), 2L, new FieldId(3), "B")))));

        var shaped = GraphQLMutationResultShaper.shape(materialized.roots().get(0), customerShape(ACCOUNTS, true));

        assertEquals(List.of("customerId", "displayName", "accounts"), new ArrayList<>(shaped.keySet()));
    }

    @Test
    void zero_affected_root_shapes_as_null() {
        var model = model(ACCOUNTS, "Accounts", RelationshipCardinality.MANY);
        var intent = new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.UPDATE, List.of(), null, List.of(new FieldId(1))), List.of());
        var materialized = new MutationResultMaterializer(model).materialize(
                intent, new MutationBatchResult(List.of(new MutationResult(0, new LinkedHashMap<>()))));

        assertNull(GraphQLMutationResultShaper.shapeRoot(materialized, customerShape(ACCOUNTS, true)));
    }

    @Test
    void no_roots_shapes_as_null() {
        assertNull(GraphQLMutationResultShaper.shapeRoot(MutationMaterializedResult.EMPTY, customerShape(ACCOUNTS, true)));
    }

    @Test
    void missing_singular_nested_result_shapes_as_null() {
        var model = model(PRIMARY, "Primary", RelationshipCardinality.ONE);
        var intent = new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.CREATE, List.of(), null, List.of(new FieldId(1))), List.of());
        var materialized = new MutationResultMaterializer(model).materialize(
                intent, new MutationBatchResult(List.of(new MutationResult(1, values(new FieldId(1), 42L)))));

        var shaped = GraphQLMutationResultShaper.shapeRoot(materialized, customerShape(PRIMARY, false));

        assertTrue(shaped.containsKey("primary"));
        assertNull(shaped.get("primary"));
    }

    @Test
    void zero_affected_singular_nested_result_shapes_as_null() {
        var model = model(PRIMARY, "Primary", RelationshipCardinality.ONE);
        var materialized = new MutationResultMaterializer(model).materialize(
                nested(PRIMARY, 1),
                new MutationBatchResult(List.of(
                        new MutationResult(1, values(new FieldId(1), 42L)),
                        new MutationResult(0, new LinkedHashMap<>()))));

        var shaped = GraphQLMutationResultShaper.shapeRoot(materialized, customerShape(PRIMARY, false));

        assertNull(shaped.get("primary"));
    }

    @Test
    void collection_drops_children_with_no_materialized_values() {
        var model = model(ACCOUNTS, "Accounts", RelationshipCardinality.MANY);
        var materialized = new MutationResultMaterializer(model).materialize(
                nested(ACCOUNTS, 2),
                new MutationBatchResult(List.of(
                        new MutationResult(1, values(new FieldId(1), 42L)),
                        new MutationResult(0, new LinkedHashMap<>()),
                        new MutationResult(1, values(new FieldId(1), 9L, new FieldId(3), "Kept")))));

        var shaped = GraphQLMutationResultShaper.shapeRoot(materialized, customerShape(ACCOUNTS, true));

        @SuppressWarnings("unchecked")
        var accounts = (List<Object>) shaped.get("accounts");
        assertEquals(1, accounts.size());
        assertEquals(9L, ((Map<?, ?>) accounts.get(0)).get("accountId"));
    }

    @Test
    void field_missing_from_the_materialized_values_shapes_as_null() {
        var model = model(ACCOUNTS, "Accounts", RelationshipCardinality.MANY);
        var intent = new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.CREATE, List.of(), null, List.of(new FieldId(1))), List.of());
        var materialized = new MutationResultMaterializer(model).materialize(
                intent, new MutationBatchResult(List.of(new MutationResult(1, values(new FieldId(1), 5L)))));

        var shaped = GraphQLMutationResultShaper.shapeRoot(materialized, customerShape(ACCOUNTS, true));

        assertEquals(5L, shaped.get("customerId"));
        assertTrue(shaped.containsKey("displayName"));
        assertNull(shaped.get("displayName"));
    }

    @Test
    void two_argument_field_constructor_uses_the_response_name_for_both_names() {
        var field = new GraphQLMutationResultField(new FieldId(1), "id");
        assertEquals("id", field.graphQLName());
        assertEquals("id", field.alias());
        assertEquals("id", field.responseName());
    }

    @Test
    void arguments_are_required() {
        assertThrows(NullPointerException.class, () -> GraphQLMutationResultShaper.shape(null, accountShape()));
        assertThrows(NullPointerException.class, () -> GraphQLMutationResultShaper.shapeRoot(null, accountShape()));
        assertThrows(NullPointerException.class,
                () -> GraphQLMutationResultShaper.shapeRoot(MutationMaterializedResult.EMPTY, null));
    }

    @Test
    void adaptation_result_aliases_the_result_shape() {
        var shape = accountShape();
        var adaptation = new GraphQLMutationAdaptation(nested(ACCOUNTS, 0), shape);
        assertEquals(shape, adaptation.result());
        assertEquals("k", new GraphQLMutationBatchItem("k", adaptation).resultKey());
    }
}
