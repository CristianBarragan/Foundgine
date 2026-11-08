package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.MutationEntitySchema;
import com.foundgine.core.abstractions.MutationRelationshipSchema;
import com.foundgine.core.abstractions.MutationSchema;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.mutation.SemanticMutationKind;
import com.foundgine.core.semantic.planning.mutation.IMutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationFieldValue;
import com.foundgine.core.semantic.planning.mutation.MutationIntent;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import com.foundgine.core.semantic.planning.mutation.NestedMutationChild;
import com.foundgine.core.semantic.planning.mutation.NestedMutationIntent;
import com.foundgine.core.semantic.planning.mutation.UpsertIntent;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Tests for {@link GraphQLMutationSemanticConverter}, the security boundary between the adapter and the engine. */
class GraphQLMutationSemanticConverterTest {
    private static final EntityId CUSTOMER = new EntityId(1);
    private static final EntityId ACCOUNT = new EntityId(2);
    private static final RelationshipId ACCOUNTS = new RelationshipId(10);
    private static final FieldId ID = new FieldId(1);
    private static final FieldId NAME = new FieldId(2);
    private static final FieldId ACCOUNT_CUSTOMER_ID = new FieldId(2);
    private static final FieldId ACCOUNT_NAME = new FieldId(3);
    private static final ColumnId C1 = new ColumnId(1);
    private static final ColumnId C2 = new ColumnId(2);
    private static final ColumnId C3 = new ColumnId(3);

    /** Customer(Id=C1 pk, Name=C2); Account(Id=C1 pk, CustomerId=C2, Name=C3); Customer.Accounts: C1 -> C2. */
    private static MutationSchema schema(ColumnId customerPk, ColumnId relationshipSource) {
        var customer = new MutationEntitySchema(
                CUSTOMER, "Customer", new LinkedHashSet<>(List.of(C1, C2)), map(ID, C1, NAME, C2), customerPk);
        var account = new MutationEntitySchema(
                ACCOUNT, "Account", new LinkedHashSet<>(List.of(C1, C2, C3)),
                map(ID, C1, ACCOUNT_CUSTOMER_ID, C2, ACCOUNT_NAME, C3), C1);
        var relationship = new MutationRelationshipSchema(ACCOUNTS, CUSTOMER, ACCOUNT, "Accounts", relationshipSource, C2);
        return new MutationSchema() {
            @Override
            public MutationEntitySchema getEntity(EntityId id) {
                return id.equals(CUSTOMER) ? customer : account;
            }

            @Override
            public MutationRelationshipSchema getRelationship(RelationshipId id) {
                return relationship;
            }
        };
    }

    private static MutationSchema schema() {
        return schema(C1, C1);
    }

    private static Map<FieldId, ColumnId> map(Object... kv) {
        var m = new LinkedHashMap<FieldId, ColumnId>();
        for (int i = 0; i < kv.length; i += 2) m.put((FieldId) kv[i], (ColumnId) kv[i + 1]);
        return m;
    }

    private static NestedMutationIntent customerCreate(List<FieldId> returns, NestedMutationChild... children) {
        return new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.CREATE,
                        List.of(new MutationFieldValue(C2, "Ada")), null, returns),
                List.of(children));
    }

    private static NestedMutationChild accountChild(List<MutationFieldValue> fields) {
        return new NestedMutationChild(ACCOUNTS, new NestedMutationIntent(
                new MutationIntent(ACCOUNT, MutationKind.CREATE, fields, null, List.of(ID)), List.of()));
    }

    @Test
    void flat_create_maps_columns_to_semantic_fields_and_return_fields() {
        var graph = GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(ID, NAME))), schema());

        var op = graph.operations().get(0);
        assertEquals(1, graph.operations().size());
        assertEquals(SemanticMutationKind.CREATE, op.kind());
        assertEquals(CUSTOMER, op.entity());
        assertEquals(NAME, op.fields().get(0).field());
        assertEquals("Ada", op.fields().get(0).value());
        assertNull(op.fields().get(0).source());
        assertEquals(List.of(ID, NAME), op.returnFields());
        assertTrue(op.dependencies().isEmpty());
    }

    @Test
    void nested_child_receives_parent_key_reference_and_dependency() {
        var graph = GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(ID), accountChild(List.of(new MutationFieldValue(C3, "Checking"))))),
                schema());

        assertEquals(2, graph.operations().size());
        var child = graph.operations().get(1);
        assertEquals(ACCOUNT, child.entity());
        assertEquals(2, child.fields().size());
        var propagated = child.fields().get(1);
        assertEquals(ACCOUNT_CUSTOMER_ID, propagated.field());
        assertNull(propagated.value());
        assertEquals(0, propagated.source().sourceOperationIndex());
        assertEquals(ID, propagated.source().sourceField());

        var dependency = child.dependencies().get(0);
        assertEquals(0, dependency.sourceOperationIndex());
        assertEquals(1, dependency.targetOperationIndex());
        assertEquals(ID, dependency.sourceField());
        assertEquals(ACCOUNT_CUSTOMER_ID, dependency.targetField());
        assertEquals(ACCOUNTS, dependency.relationship());
    }

    @Test
    void existing_source_reference_on_the_relationship_field_is_not_duplicated() {
        var preset = MutationFieldValue.fromPrevious(C2, 0, ID);
        var graph = GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(ID), accountChild(List.of(preset)))), schema());

        var child = graph.operations().get(1);
        assertEquals(1, child.fields().size());
        assertEquals(1, child.dependencies().size());
    }

    @Test
    void batch_roots_are_flattened_in_order_with_continuing_indices() {
        var graph = GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(ID)),
                        customerCreate(List.of(ID), accountChild(List.of()))),
                schema());

        assertEquals(3, graph.operations().size());
        assertEquals(CUSTOMER, graph.operations().get(0).entity());
        assertEquals(CUSTOMER, graph.operations().get(1).entity());
        assertEquals(1, graph.operations().get(2).dependencies().get(0).sourceOperationIndex());
        assertEquals(2, graph.operations().get(2).dependencies().get(0).targetOperationIndex());
    }

    @Test
    void parent_must_return_its_primary_key() {
        var ex = assertThrows(IllegalStateException.class, () -> GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(NAME), accountChild(List.of()))), schema()));
        assertTrue(ex.getMessage().contains("must return its primary key field '1'"), ex.getMessage());
    }

    @Test
    void child_must_not_explicitly_supply_the_relationship_field() {
        var ex = assertThrows(IllegalStateException.class, () -> GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(ID), accountChild(List.of(new MutationFieldValue(C2, 99L))))),
                schema()));
        assertTrue(ex.getMessage().contains("explicitly supplies relationship field '2'"), ex.getMessage());
    }

    @Test
    void relationship_must_originate_at_the_parent_primary_key() {
        var ex = assertThrows(IllegalStateException.class, () -> GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(ID), accountChild(List.of()))), schema(C1, C2)));
        assertTrue(ex.getMessage().contains("must originate at the parent primary key"), ex.getMessage());
    }

    @Test
    void parent_without_a_primary_key_is_rejected() {
        var ex = assertThrows(IllegalStateException.class, () -> GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(ID), accountChild(List.of()))), schema(null, C1)));
        assertTrue(ex.getMessage().contains("requires a primary key"), ex.getMessage());
    }

    @Test
    void relationship_must_connect_the_parent_and_child_entities() {
        var wrongChild = new NestedMutationChild(ACCOUNTS, new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.CREATE, List.of(), null, List.of(ID)), List.of()));
        var ex = assertThrows(IllegalStateException.class, () -> GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(customerCreate(List.of(ID), wrongChild)), schema()));
        assertTrue(ex.getMessage().contains("does not connect '1' to '1'"), ex.getMessage());
    }

    @Test
    void empty_batch_is_rejected() {
        var ex = assertThrows(IllegalStateException.class,
                () -> GraphQLMutationSemanticConverter.toSemanticGraph(List.of(), schema()));
        assertEquals("A GraphQL mutation batch must contain at least one mutation intent.", ex.getMessage());
    }

    @Test
    void unmapped_column_is_rejected() {
        var intent = new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.CREATE,
                        List.of(new MutationFieldValue(new ColumnId(99), "x")), null, List.of(ID)), List.of());
        var ex = assertThrows(IllegalStateException.class,
                () -> GraphQLMutationSemanticConverter.toSemanticGraph(List.of(intent), schema()));
        assertTrue(ex.getMessage().contains("column '99' is not mapped to a semantic field on 'Customer'"), ex.getMessage());
    }

    @Test
    void update_carries_the_filter_and_delete_requires_one() {
        var filter = new SemanticFieldFilter(ID, SemanticFilterOperator.EQ, 99L);
        var update = new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.UPDATE,
                        List.of(new MutationFieldValue(C2, "Ada")), filter, List.of(ID)), List.of());
        var op = GraphQLMutationSemanticConverter.toSemanticGraph(List.of(update), schema()).operations().get(0);
        assertEquals(SemanticMutationKind.UPDATE, op.kind());
        assertEquals(filter, op.filter());

        var delete = new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.DELETE, List.of(), filter, List.of(ID)), List.of());
        assertEquals(SemanticMutationKind.DELETE,
                GraphQLMutationSemanticConverter.toSemanticGraph(List.of(delete), schema()).operations().get(0).kind());

        var unfiltered = new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.DELETE, List.of(), null, List.of(ID)), List.of());
        var ex = assertThrows(IllegalStateException.class,
                () -> GraphQLMutationSemanticConverter.toSemanticGraph(List.of(unfiltered), schema()));
        assertEquals("Delete mutation requires a filter.", ex.getMessage());
    }

    @Test
    void upsert_maps_conflict_columns_to_fields() {
        var upsert = new NestedMutationIntent(
                new UpsertIntent(CUSTOMER, List.of(new MutationFieldValue(C2, "Ada")), List.of(C2), List.of(ID)),
                List.of());
        var op = GraphQLMutationSemanticConverter.toSemanticGraph(List.of(upsert), schema()).operations().get(0);
        assertEquals(SemanticMutationKind.UPSERT, op.kind());
        assertEquals(List.of(NAME), op.conflictFields());

        var noConflicts = new NestedMutationIntent(new UpsertIntent(CUSTOMER, List.of()), List.of());
        assertTrue(GraphQLMutationSemanticConverter.toSemanticGraph(List.of(noConflicts), schema())
                .operations().get(0).conflictFields().isEmpty());
    }

    @Test
    void unconvertible_intents_are_not_supported() {
        var upsertKindOnPlainIntent = new NestedMutationIntent(
                new MutationIntent(CUSTOMER, MutationKind.UPSERT, List.of(), null, List.of(ID)), List.of());
        assertThrows(UnsupportedOperationException.class,
                () -> GraphQLMutationSemanticConverter.toSemanticGraph(List.of(upsertKindOnPlainIntent), schema()));

        IMutationIntent custom = new IMutationIntent() {
            public EntityId entity() { return CUSTOMER; }
            public MutationKind kind() { return MutationKind.CREATE; }
            public List<MutationFieldValue> fields() { return List.of(); }
        };
        assertThrows(UnsupportedOperationException.class, () -> GraphQLMutationSemanticConverter.toSemanticGraph(
                List.of(new NestedMutationIntent(custom, List.of())), schema()));
    }

    @Test
    void arguments_are_required() {
        assertThrows(NullPointerException.class, () -> GraphQLMutationSemanticConverter.toSemanticGraph(null, schema()));
        assertThrows(NullPointerException.class,
                () -> GraphQLMutationSemanticConverter.toSemanticGraph(List.of(customerCreate(List.of(ID))), null));
    }
}
