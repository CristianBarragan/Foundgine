package com.foundgine.providers.storage.orm;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionIR;
import com.foundgine.core.execution.ExecutionIRNode;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.core.semantic.planning.ExecutionOperation;
import com.foundgine.core.semantic.planning.SemanticPlanAuthorizationBinding;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticSortDirection;

import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;

import org.junit.jupiter.api.Test;

class OrmExecutionProviderTest {
    static final class Customer {}

    static final class Order {}

    static final class Tag {}

    static final class Line {}

    static final class Doc {}

    static final EntityId CUSTOMER = new EntityId(1);
    static final EntityId ORDER = new EntityId(2);
    static final EntityId TAG = new EntityId(3);
    static final EntityId LINE = new EntityId(4);
    static final EntityId DOC = new EntityId(5);

    static final FieldId C_ID = new FieldId(11);
    static final FieldId C_NAME = new FieldId(12);
    static final FieldId C_REGION = new FieldId(13);
    static final FieldId C_SECRET = new FieldId(14);
    static final FieldId O_ID = new FieldId(21);
    static final FieldId O_CUSTOMER = new FieldId(22);
    static final FieldId O_TOTAL = new FieldId(23);
    static final FieldId T_ID = new FieldId(31);
    static final FieldId T_CUSTOMER = new FieldId(32);
    static final FieldId T_LABEL = new FieldId(33);
    static final FieldId L_ID = new FieldId(41);
    static final FieldId L_ORDER = new FieldId(42);
    static final FieldId L_SKU = new FieldId(43);
    static final FieldId D_ID = new FieldId(51);
    static final FieldId D_NAME = new FieldId(52);

    static final RelationshipId C_ORDERS = new RelationshipId(1);
    static final RelationshipId C_TAGS = new RelationshipId(2);
    static final RelationshipId O_LINES = new RelationshipId(3);

    static ColumnReference ref(EntityId e, int c) {
        return new ColumnReference(e, new ColumnId(c));
    }

    static EntityMetadata entity(EntityId id, String name, int pkCol, List<FieldMetadata> fields) {
        var cols = fields.stream().map(f -> new ColumnMetadata(f.column().columnId(), f.name())).toList();
        return new EntityMetadata(id, name, cols, null, fields, ref(id, pkCol), null, false, null, null);
    }

    static OrmRegistry registry(boolean withRelationships) {
        var entities =
                List.of(
                        entity(CUSTOMER, "Customer", 101,
                                List.of(
                                        new FieldMetadata(C_ID, "Id", Long.class, ref(CUSTOMER, 101)),
                                        new FieldMetadata(C_NAME, "Name", String.class, ref(CUSTOMER, 102)),
                                        new FieldMetadata(C_REGION, "Region", String.class, ref(CUSTOMER, 103)),
                                        new FieldMetadata(C_SECRET, "Secret", String.class, ref(CUSTOMER, 104)))),
                        entity(ORDER, "Order", 201,
                                List.of(
                                        new FieldMetadata(O_ID, "Id", Long.class, ref(ORDER, 201)),
                                        new FieldMetadata(O_CUSTOMER, "CustomerId", Integer.class, ref(ORDER, 202)),
                                        new FieldMetadata(O_TOTAL, "Total", BigDecimal.class, ref(ORDER, 203)))),
                        entity(TAG, "Tag", 301,
                                List.of(
                                        new FieldMetadata(T_ID, "Id", Long.class, ref(TAG, 301)),
                                        new FieldMetadata(T_CUSTOMER, "CustomerId", Long.class, ref(TAG, 302)),
                                        new FieldMetadata(T_LABEL, "Label", String.class, ref(TAG, 303)))),
                        entity(LINE, "Line", 401,
                                List.of(
                                        new FieldMetadata(L_ID, "Id", Long.class, ref(LINE, 401)),
                                        new FieldMetadata(L_ORDER, "OrderId", Long.class, ref(LINE, 402)),
                                        new FieldMetadata(L_SKU, "Sku", String.class, ref(LINE, 403)))),
                        entity(DOC, "Doc", 501,
                                List.of(
                                        new FieldMetadata(D_ID, "Id", UUID.class, ref(DOC, 501)),
                                        new FieldMetadata(D_NAME, "Name", String.class, ref(DOC, 502)))));
        var rels =
                withRelationships
                        ? List.of(
                                new RelationshipMetadata(C_ORDERS, CUSTOMER, ORDER, "Orders", ref(CUSTOMER, 101), ref(ORDER, 202)),
                                new RelationshipMetadata(C_TAGS, CUSTOMER, TAG, "Tags", ref(CUSTOMER, 101), ref(TAG, 302)),
                                new RelationshipMetadata(O_LINES, ORDER, LINE, "Lines", ref(ORDER, 201), ref(LINE, 402)))
                        : List.<RelationshipMetadata>of();
        return OrmRegistry.create(
                entities,
                rels,
                Map.of(
                        "Customer", Customer.class, "Order", Order.class, "Tag", Tag.class,
                        "Line", Line.class, "Doc", Doc.class));
    }

    static final class Counting implements IOrmQuerySource {
        final InMemoryOrmQuerySource inner;
        final List<OrmFetchRequest> requests = new ArrayList<>();

        Counting(InMemoryOrmQuerySource inner) {
            this.inner = inner;
        }

        @Override
        public List<Map<FieldId, Object>> fetch(OrmFetchRequest r) {
            requests.add(r);
            return inner.fetch(r);
        }

        long forEntity(EntityId e) {
            return requests.stream().filter(r -> r.entity().entityId().equals(e)).count();
        }
    }

    static InMemoryOrmQuerySource seed() {
        var s = new InMemoryOrmQuerySource();
        s.add(CUSTOMER, Map.of(C_ID, 1L, C_NAME, "Ann", C_REGION, "N", C_SECRET, "s1"));
        s.add(CUSTOMER, Map.of(C_ID, 2L, C_NAME, "Bob", C_REGION, "S", C_SECRET, "s2"));
        s.add(CUSTOMER, Map.of(C_ID, 3L, C_NAME, "Cy", C_REGION, "N", C_SECRET, "s3"));
        s.add(ORDER, Map.of(O_ID, 10L, O_CUSTOMER, 1, O_TOTAL, new BigDecimal("5.00")));
        s.add(ORDER, Map.of(O_ID, 11L, O_CUSTOMER, 1, O_TOTAL, new BigDecimal("7.50")));
        s.add(ORDER, Map.of(O_ID, 12L, O_CUSTOMER, 2, O_TOTAL, new BigDecimal("1.00")));
        s.add(TAG, Map.of(T_ID, 100L, T_CUSTOMER, 1L, T_LABEL, "vip"));
        s.add(TAG, Map.of(T_ID, 101L, T_CUSTOMER, 1L, T_LABEL, "new"));
        s.add(LINE, Map.of(L_ID, 1000L, L_ORDER, 10L, L_SKU, "A"));
        s.add(LINE, Map.of(L_ID, 1001L, L_ORDER, 10L, L_SKU, "B"));
        return s;
    }

    static ExecutionIRNode node(int id, ExecutionOperation op, EntityId e, List<FieldId> fields,
            RelationshipId via, List<ExecutionIRNode> kids, SemanticQueryOptions o, AuthorizationPredicate a) {
        return new ExecutionIRNode(id, op, e, fields, via, null, kids, o, a, null);
    }

    static ExecutionIRNode scan(EntityId e, List<FieldId> fields, List<ExecutionIRNode> kids,
            SemanticQueryOptions o, AuthorizationPredicate a) {
        return node(0, ExecutionOperation.SCAN, e, fields, null, kids, o, a);
    }

    static ExecutionIRNode child(int id, EntityId e, List<FieldId> fields, RelationshipId via,
            List<ExecutionIRNode> kids, AuthorizationPredicate a) {
        return node(id, ExecutionOperation.TRAVERSE, e, fields, via, kids, null, a);
    }

    static ExecutionIR ir(ExecutionIRNode root) {
        return new ExecutionIR(root, List.of(), new SemanticPlanAuthorizationBinding("c", "a"));
    }

    static ExecutionResult run(IOrmQuerySource src, OrmRegistry reg, ExecutionIRNode root, ExecutionContext ctx) {
        var compiler = new OrmPlanCompiler("orm-test", reg);
        var provider = new OrmExecutionProvider("orm-test", src, reg);
        return provider.execute(compiler.compile(ir(root)), ctx, CancellationToken.NONE);
    }

    static ExecutionResult run(ExecutionIRNode root) {
        return run(seed(), registry(true), root, ExecutionContext.EMPTY);
    }

    static Object cell(ExecutionRow row, int node, FieldId f) {
        return row.effectiveCells().entrySet().stream()
                .filter(e -> e.getKey().nodeId() == node && e.getKey().fieldId().equals(f))
                .map(Map.Entry::getValue).findFirst().orElse("<absent>");
    }

    static SemanticQueryOptions opts(com.foundgine.core.semantic.query.SemanticFilterExpression filter,
            List<SemanticOrderTerm> order, Integer limit, Integer offset, String after) {
        return new SemanticQueryOptions(filter, order, limit, offset, after);
    }

    // ------------------------------------------------------------------ scans

    @Test
    void scan_projects_only_selected_fields() {
        var r = run(scan(CUSTOMER, List.of(C_NAME), List.of(), null, null));
        assertEquals(3, r.rows().size());
        var row = r.rows().get(0);
        assertEquals(1, row.effectiveCells().size());
        assertEquals(Map.of("Name", "Ann"), row.values());
        assertFalse(row.values().containsValue("s1"));
    }

    @Test
    void filter_order_offset_limit() {
        var o = opts(new SemanticFieldFilter(C_REGION, SemanticFilterOperator.EQ, "N"),
                List.of(new SemanticOrderTerm(C_NAME, SemanticSortDirection.DESC)), 1, 0, null);
        var r = run(scan(CUSTOMER, List.of(C_ID, C_NAME), List.of(), o, null));
        assertEquals(1, r.rows().size());
        assertEquals("Cy", cell(r.rows().get(0), 0, C_NAME));
    }

    @Test
    void in_filter_matches_across_numeric_types() {
        var o = opts(new SemanticFieldFilter(C_ID, SemanticFilterOperator.IN, List.of(2, 3)),
                List.of(), null, null, null);
        var r = run(scan(CUSTOMER, List.of(C_ID), List.of(), o, null));
        assertEquals(2, r.rows().size());
    }

    // ------------------------------------------------------------------ authorization

    static AuthorizationPredicate regionIsContext() {
        return AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("r"), "Region"),
                AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("tenant"), "region"));
    }

    @Test
    void authorization_uses_context_value() {
        var r = run(seed(), registry(true), scan(CUSTOMER, List.of(C_NAME), List.of(), null, regionIsContext()),
                new ExecutionContext(Map.of("tenant.region", "N")));
        assertEquals(2, r.rows().size());
        assertEquals(List.of(0), r.evidence().authorizedNodeIds());
    }

    @Test
    void missing_context_value_fails_closed() {
        assertThrows(IllegalStateException.class,
                () -> run(seed(), registry(true), scan(CUSTOMER, List.of(C_NAME), List.of(), null, regionIsContext()),
                        ExecutionContext.EMPTY));
    }

    @Test
    void user_filter_cannot_widen_authorization() {
        var o = opts(new SemanticFieldFilter(C_REGION, SemanticFilterOperator.NEQ, "zzz"), List.of(), null, null, null);
        var r = run(seed(), registry(true), scan(CUSTOMER, List.of(C_NAME), List.of(), o, regionIsContext()),
                new ExecutionContext(Map.of("tenant.region", "S")));
        assertEquals(1, r.rows().size());
        assertEquals("Bob", cell(r.rows().get(0), 0, C_NAME));
    }

    // ------------------------------------------------------------------ traversal

    @Test
    void left_join_shape_cross_product_and_no_key_leak() {
        var orders = child(1, ORDER, List.of(O_TOTAL), C_ORDERS, List.of(), null);
        var tags = child(2, TAG, List.of(T_LABEL), C_TAGS, List.of(), null);
        var r = run(scan(CUSTOMER, List.of(C_NAME), List.of(orders, tags),
                opts(null, List.of(new SemanticOrderTerm(C_ID, SemanticSortDirection.ASC)), null, null, null), null));
        // Ann: 2 orders x 2 tags = 4; Bob: 1 order, no tags = 1; Cy: nothing = 1
        assertEquals(6, r.rows().size());
        assertEquals("<absent>", cell(r.rows().get(4), 2, T_LABEL));
        assertEquals("<absent>", cell(r.rows().get(5), 1, O_TOTAL));
        assertTrue(r.rows().stream().allMatch(
                row -> row.effectiveCells().keySet().stream().noneMatch(k -> k.fieldId().equals(O_CUSTOMER))));
        assertEquals(4, r.rows().stream().filter(x -> "Ann".equals(cell(x, 0, C_NAME))).count());
    }

    @Test
    void nested_traversal_and_int_long_keys() {
        var lines = child(2, LINE, List.of(L_SKU), O_LINES, List.of(), null);
        var orders = child(1, ORDER, List.of(O_ID), C_ORDERS, List.of(lines), null);
        var r = run(scan(CUSTOMER, List.of(C_NAME), List.of(orders), null, null));
        // Order.CustomerId is Integer, Customer.Id is Long: keys must still match.
        // Ann o10 x2 lines, Ann o11, Bob o12, Cy
        assertEquals(5, r.rows().size());
    }

    @Test
    void traversal_from_the_child_side() {
        var customer = child(1, CUSTOMER, List.of(C_NAME), C_ORDERS, List.of(), null);
        var r = run(scan(ORDER, List.of(O_ID), List.of(customer), null, null));
        assertEquals(3, r.rows().size());
        assertEquals("Bob", cell(r.rows().get(2), 1, C_NAME));
    }

    @Test
    void child_authorization_is_applied_and_empty_parents_skip_child_query() {
        var src = new Counting(seed());
        var deny = AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("r"), "Label"),
                AuthorizationPredicate.constant("vip"));
        var tags = child(1, TAG, List.of(T_LABEL), C_TAGS, List.of(), deny);
        var none = opts(new SemanticFieldFilter(C_ID, SemanticFilterOperator.EQ, 99), List.of(), null, null, null);
        var r = run(src, registry(true), scan(CUSTOMER, List.of(C_NAME), List.of(tags), none, null), ExecutionContext.EMPTY);
        assertEquals(0, r.rows().size());
        assertEquals(0, src.forEntity(TAG));

        var r2 = run(src, registry(true), scan(CUSTOMER, List.of(C_NAME), List.of(tags),
                opts(new SemanticFieldFilter(C_ID, SemanticFilterOperator.EQ, 1), List.of(), null, null, null), null),
                ExecutionContext.EMPTY);
        assertEquals(1, r2.rows().size());
        assertEquals("vip", cell(r2.rows().get(0), 1, T_LABEL));
    }

    @Test
    void child_keys_are_chunked_at_500() {
        var s = new InMemoryOrmQuerySource();
        for (long i = 1; i <= 1200; i++) s.add(CUSTOMER, Map.of(C_ID, i, C_NAME, "n" + i));
        var src = new Counting(s);
        var orders = child(1, ORDER, List.of(O_TOTAL), C_ORDERS, List.of(), null);
        var r = run(src, registry(true), scan(CUSTOMER, List.of(C_NAME), List.of(orders), null, null), ExecutionContext.EMPTY);
        assertEquals(1200, r.rows().size());
        assertEquals(3, src.forEntity(ORDER));
    }

    @Test
    void traversal_without_relationships_fails_with_clear_message() {
        var orders = child(1, ORDER, List.of(O_ID), C_ORDERS, List.of(), null);
        var ex = assertThrows(IllegalStateException.class,
                () -> run(seed(), registry(false), scan(CUSTOMER, List.of(C_NAME), List.of(orders), null, null), ExecutionContext.EMPTY));
        assertTrue(ex.getMessage().contains("relationship metadata"));
    }

    // ------------------------------------------------------------------ cursors

    @Test
    void keyset_paging_mixed_order_walks_every_row_once() {
        var s = new InMemoryOrmQuerySource();
        String[] regions = {"N", "S", "N", "S", "N", "S", "N"};
        for (int i = 0; i < regions.length; i++) s.add(CUSTOMER, Map.of(C_ID, (long) i + 1, C_NAME, "n", C_REGION, regions[i]));
        var order = List.of(new SemanticOrderTerm(C_REGION, SemanticSortDirection.DESC),
                new SemanticOrderTerm(C_NAME, SemanticSortDirection.ASC));
        var seen = new ArrayList<Object>();
        String after = null;
        for (int page = 0; page < 10; page++) {
            var r = run(s, registry(true), scan(CUSTOMER, List.of(C_ID), List.of(), opts(null, order, 3, null, after), null), ExecutionContext.EMPTY);
            r.rows().forEach(row -> seen.add(cell(row, 0, C_ID)));
            assertEquals(page > 0, r.pageInfo().hasPreviousPage());
            if (!r.pageInfo().hasNextPage()) break;
            after = r.pageInfo().endCursor();
        }
        assertEquals(7, seen.size());
        assertEquals(7, seen.stream().distinct().count());
        // S rows (ids 2,4,6) come first because region is DESC, then N rows by primary key.
        assertEquals(List.of(2L, 4L, 6L, 1L, 3L, 5L, 7L), seen);
    }

    @Test
    void private_aggregate_order_values_travel_separately_from_semantic_projection() {
        var registry = registry(true);
        var link = registry.link(C_ORDERS, CUSTOMER, ORDER);
        var order = new OrmOrder(O_ID, true, OrmOrder.AggregateKind.COUNT,
                registry.map(ORDER), link, new OrmPredicate.Const(true));
        var request = new OrmFetchRequest(registry.map(CUSTOMER), List.of(C_NAME),
                List.of(), List.of(order), null, null);

        var fetched = seed().fetchWithPrivateOrderValues(request);

        assertEquals(List.of("Ann", "Bob", "Cy"),
                fetched.rows().stream().map(row -> row.get(C_NAME)).toList());
        assertTrue(fetched.rows().stream().allMatch(row -> !row.containsKey(C_ID)));
        assertEquals(List.of(List.of(2L), List.of(1L), List.of(0L)), fetched.privateOrderValues());
    }

    @Test
    void next_page_detection_counts_root_rows_not_joined_rows() {
        var orders = child(1, ORDER, List.of(O_TOTAL), C_ORDERS, List.of(), null);
        var r = run(scan(CUSTOMER, List.of(C_NAME), List.of(orders),
                opts(null, List.of(new SemanticOrderTerm(C_ID, SemanticSortDirection.ASC)), 2, null, null), null));
        assertTrue(r.pageInfo().hasNextPage()); // 3 customers, limit 2
        assertEquals(3, r.rows().size()); // Ann x2 orders + Bob
    }

    @Test
    void forged_cursor_cannot_leave_authorization() {
        // Position before everything, but the caller may only see region S.
        String forged = OrmCursor.encode(List.of(0L));
        var r = run(seed(), registry(true), scan(CUSTOMER, List.of(C_NAME), List.of(),
                opts(null, List.of(), 10, null, forged), regionIsContext()),
                new ExecutionContext(Map.of("tenant.region", "S")));
        assertEquals(1, r.rows().size());
    }

    @Test
    void cursor_on_unorderable_or_null_positions_is_refused() {
        var docs = scan(DOC, List.of(D_NAME), List.of(), opts(null, List.of(), 1, null, OrmCursor.encode(List.of("x"))), null);
        assertThrows(UnsupportedOperationException.class,
                () -> new OrmPlanCompiler("orm-test", registry(true)).compile(ir(docs)));
        var nullPos = scan(CUSTOMER, List.of(C_NAME), List.of(), opts(null, List.of(), 1, null, OrmCursor.encode(java.util.Arrays.asList((Object) null))), null);
        assertThrows(UnsupportedOperationException.class,
                () -> run(seed(), registry(true), nullPos, ExecutionContext.EMPTY));
    }

    @Test
    void malformed_cursor_is_rejected() {
        var bad = scan(CUSTOMER, List.of(C_NAME), List.of(), opts(null, List.of(), 1, null, "!!!"), null);
        assertThrows(IllegalStateException.class, () -> run(seed(), registry(true), bad, ExecutionContext.EMPTY));
    }

    // ------------------------------------------------------------------ plan compiler

    @Test
    void child_paging_is_refused_and_conformance_is_reported() {
        var kid = node(1, ExecutionOperation.TRAVERSE, ORDER, List.of(O_ID), C_ORDERS, List.of(),
                opts(null, List.of(), 5, null, null), null);
        assertThrows(UnsupportedOperationException.class,
                () -> new OrmPlanCompiler("orm-test", registry(true)).compile(ir(scan(CUSTOMER, List.of(C_ID), List.of(kid), null, null))));

        var compiler = new OrmPlanCompiler("orm-test", registry(true));
        var good = new ExecutionIR(scan(CUSTOMER, List.of(C_ID), List.of(), null, null),
                List.of("authorization.required", "mutation.atomic"), new SemanticPlanAuthorizationBinding("c", "a"));
        var result = compiler.evaluate(good, compiler.compile(good));
        assertEquals(List.of("authorization.required"), result.satisfied());
        assertFalse(result.isSatisfied());
    }
}
