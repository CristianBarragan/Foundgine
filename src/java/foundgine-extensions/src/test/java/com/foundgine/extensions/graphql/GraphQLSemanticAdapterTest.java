package com.foundgine.extensions.graphql;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticModelBuilder;
import com.foundgine.core.semantic.query.SemanticAggregateFilter;
import com.foundgine.core.semantic.query.SemanticAggregateFilterOperator;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterAggregate;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticOrFilter;
import com.foundgine.core.semantic.query.SemanticOrderAggregate;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/**
 * Port of the C# query-side GraphQL tests: HotChocolateSemanticAdapterTests, QueryArgumentsTests, AggregateFilterTests,
 * CollectionOrderingTests, RelationshipOrderingTests, GraphQLOperationTests (query half), GraphQLErrorSemanticsTests
 * (query half) and AdapterBoundaryTests.
 */
class GraphQLSemanticAdapterTest {
    private static SemanticModel customerOnly() {
        return new SemanticModelBuilder()
                .entity(new EntityId(1), "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class))
                .build();
    }

    private static SemanticModel customerWithAccounts() {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var transaction = new EntityId(3);
        return new SemanticModelBuilder()
                .entity(customer, "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "Name", String.class)
                        .relationship(new RelationshipId(1), "Accounts", account, RelationshipCardinality.MANY))
                .entity(account, "Account", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(3), "Balance", java.math.BigDecimal.class)
                        .relationship(new RelationshipId(2), "Transactions", transaction, RelationshipCardinality.MANY))
                .entity(transaction, "Transaction", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(3), "Amount", java.math.BigDecimal.class))
                .build();
    }

    // ----- HotChocolateSemanticAdapterTests -----------------------------------------------------------------------

    @Test
    void graphql_query_translates_to_provider_neutral_semantic_request() {
        var request = new GraphQLSemanticAdapter(customerWithAccounts()).adapt("""
                query { customer { id name accounts { id transactions { id amount } } } }
                """);

        assertEquals(new EntityId(1), request.root());
        assertEquals(3, request.selections().size());
        assertEquals(new FieldId(1), request.selections().get(0).field());
        assertEquals(new FieldId(2), request.selections().get(1).field());

        var accounts = request.selections().get(2);
        assertEquals(new RelationshipId(1), accounts.relationship());
        assertEquals(2, accounts.children().size());
        assertEquals(new FieldId(1), accounts.children().get(0).field());

        var transactions = accounts.children().get(1);
        assertEquals(new RelationshipId(2), transactions.relationship());
        assertEquals(
                List.of(new FieldId(1), new FieldId(3)),
                transactions.children().stream().map(x -> x.field()).toList());
    }

    @Test
    void inline_fragment_is_transparent_to_semantic_resolution() {
        var request = new GraphQLSemanticAdapter(customerOnly()).adapt("""
                query { customer { id ... on Customer { name } } }
                """);
        assertEquals(List.of(new FieldId(1), new FieldId(2)), request.selections().stream().map(x -> x.field()).toList());
    }

    @Test
    void unknown_graphql_field_is_rejected() {
        var ex = assertThrows(IllegalStateException.class,
                () -> new GraphQLSemanticAdapter(customerOnly()).adapt("query { customer { doesNotExist } }"));
        assertTrue(ex.getMessage().contains("doesNotExist"));
    }

    @Test
    void alias_is_preserved_in_result_shape_without_entering_semantic_request() {
        var adaptation = new GraphQLSemanticAdapter(customerOnly())
                .adaptResultShape("query { customer { customerId: id } }");
        assertEquals(1, adaptation.request().selections().size());
        assertEquals(new FieldId(1), adaptation.request().selections().get(0).field());
        assertEquals("id", adaptation.result().fields().get(0).graphQLName());
        assertEquals("customerId", adaptation.result().fields().get(0).alias());
    }

    @Test
    void unsupported_root_argument_is_rejected() {
        var ex = assertThrows(IllegalStateException.class,
                () -> new GraphQLSemanticAdapter(customerOnly()).adapt("query { customer(id: 1) { id } }"));
        assertTrue(ex.getMessage().contains("argument"));
    }

    @Test
    void mutation_is_not_silently_translated_as_a_query() {
        var ex = assertThrows(IllegalStateException.class,
                () -> new GraphQLSemanticAdapter(customerOnly()).adapt("mutation { customer { id } }"));
        assertTrue(ex.getMessage().contains("query operations only"));
    }

    // ----- QueryArgumentsTests -------------------------------------------------------------------------------------

    @Test
    void where_order_and_paging_translate_into_semantic_options() {
        var request = new GraphQLSemanticAdapter(customerOnly()).adapt("""
                query { customer(where: { name: { eq: "Alice" } }, order: { name: DESC }, first: 10, skip: 2) { id name } }
                """);
        var options = request.options();
        assertNotNull(options);
        assertEquals(Integer.valueOf(10), options.limit());
        assertEquals(Integer.valueOf(2), options.offset());
        assertEquals(1, options.effectiveOrder().size());
        assertEquals(SemanticSortDirection.DESC, options.effectiveOrder().get(0).direction());
        assertTrue(options.filter() instanceof SemanticFieldFilter);
        var filter = (SemanticFieldFilter) options.filter();
        assertEquals(SemanticFilterOperator.EQ, filter.operator());
        assertEquals("Alice", filter.value());
    }

    @Test
    void after_cursor_translates_into_semantic_options() {
        var request = new GraphQLSemanticAdapter(customerOnly())
                .adapt("query { customer(first: 10, after: \"MQ==\") { id } }");
        assertEquals(Integer.valueOf(10), request.options().limit());
        assertEquals("MQ==", request.options().after());
    }

    @Test
    void last_and_before_are_rejected_by_the_forward_keyset_contract() {
        var adapter = new GraphQLSemanticAdapter(customerOnly());
        var ex = assertThrows(IllegalStateException.class, () -> adapter.adapt("query { customer(last: 1) { id } }"));
        assertTrue(ex.getMessage().contains("forward keyset"));
        assertThrows(IllegalStateException.class, () -> adapter.adapt("query { customer(first: -1) { id } }"));
    }

    @Test
    void and_or_in_and_multi_field_where_translate_structurally() {
        var request = new GraphQLSemanticAdapter(customerOnly()).adapt("""
                query { customer(where: { or: [ { name: { eq: "A" } }, { id: { in: [1, 2, 3] } } ] }) { id } }
                """);
        var or = (SemanticOrFilter) request.options().filter();
        assertEquals(2, or.expressions().size());
        var in = (SemanticFieldFilter) or.expressions().get(1);
        assertEquals(SemanticFilterOperator.IN, in.operator());
        assertEquals(List.of(1L, 2L, 3L), in.value());

        var and = (SemanticAndFilter) new GraphQLSemanticAdapter(customerOnly())
                .adapt("query { customer(where: { id: 7, name: { neq: \"x\" } }) { id } }").options().filter();
        assertEquals(2, and.expressions().size());
        assertEquals(SemanticFilterOperator.EQ, ((SemanticFieldFilter) and.expressions().get(0)).operator());
    }

    @Test
    void variables_feed_where_and_paging() {
        var request = new GraphQLSemanticAdapter(customerOnly()).adapt(
                "query Q($n: String!, $f: Int) { customer(where: { name: { eq: $n } }, first: $f) { id } }",
                Map.of("n", "Bob", "f", 5),
                "Q");
        assertEquals(Integer.valueOf(5), request.options().limit());
        assertEquals("Bob", ((SemanticFieldFilter) request.options().filter()).value());
    }

    @Test
    void where_must_not_be_empty_or_repeated() {
        var adapter = new GraphQLSemanticAdapter(customerOnly());
        assertThrows(IllegalStateException.class, () -> adapter.adapt("query { customer(where: {}) { id } }"));
        assertThrows(IllegalStateException.class,
                () -> adapter.adapt("query { customer(where: { id: 1 }, where: { id: 2 }) { id } }"));
    }

    // ----- Ordering: the key sequence of an input object is the order sequence (C# Dictionary insertion order) -----

    @Test
    void order_terms_keep_the_written_key_order_and_match_names_case_insensitively() {
        var request = new GraphQLSemanticAdapter(customerOnly())
                .adapt("query { customer(order: { name: ASC, ID: DESC }) { id } }");
        var order = request.options().effectiveOrder();
        assertEquals(2, order.size());
        assertEquals(new FieldId(2), order.get(0).field());
        assertEquals(SemanticSortDirection.ASC, order.get(0).direction());
        assertEquals(new FieldId(1), order.get(1).field());
        assertEquals(SemanticSortDirection.DESC, order.get(1).direction());

        var reversed = new GraphQLSemanticAdapter(customerOnly())
                .adapt("query { customer(order: { id: DESC, name: ASC }) { id } }").options().effectiveOrder();
        assertEquals(new FieldId(1), reversed.get(0).field());
        assertEquals(new FieldId(2), reversed.get(1).field());
    }

    @Test
    void case_insensitive_ordered_map_rejects_duplicates_and_is_read_only() {
        var map = new CaseInsensitiveOrderedMap();
        map.put("Zed", 1);
        map.put("alpha", 2);
        assertEquals(List.of("Zed", "alpha"), List.copyOf(map.keySet()));
        assertEquals(2, map.get("ALPHA"));
        assertTrue(map.containsKey("zED"));
        assertFalse(map.containsKey("missing"));
        map.freeze();
        assertThrows(UnsupportedOperationException.class, () -> map.put("x", 3));
        assertThrows(IllegalArgumentException.class, () -> new GraphQLSemanticAdapter(customerOnly())
                .adapt("query { customer(where: { name: \"a\", NAME: \"b\" }) { id } }"));
    }

    // ----- AggregateFilterTests ------------------------------------------------------------------------------------

    private static SemanticModel aggregateModel() {
        return new SemanticModelBuilder()
                .entity(new EntityId(401), "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .relationship(new RelationshipId(401), "Accounts", new EntityId(402), RelationshipCardinality.MANY))
                .entity(new EntityId(402), "Account", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(3), "Balance", java.math.BigDecimal.class))
                .build();
    }

    @Test
    void count_filter_is_translated_to_semantic_aggregate_filter() {
        var request = new GraphQLSemanticAdapter(aggregateModel())
                .adapt("query { customer(where: { accounts: { count: { gte: 2 } } }) { id } }");
        var filter = (SemanticAggregateFilter) request.options().filter();
        assertEquals(new RelationshipId(401), filter.relationship());
        assertEquals(SemanticFilterAggregate.COUNT, filter.aggregate());
        assertNull(filter.field());
        assertEquals(SemanticAggregateFilterOperator.GTE, filter.operator());
        assertEquals(2L, filter.value());
    }

    @Test
    void max_filter_resolves_target_field() {
        var request = new GraphQLSemanticAdapter(aggregateModel())
                .adapt("query { customer(where: { accounts: { balance: { max: { gt: 100 } } } }) { id } }");
        var filter = (SemanticAggregateFilter) request.options().filter();
        assertEquals(SemanticFilterAggregate.MAX, filter.aggregate());
        assertEquals(new FieldId(3), filter.field());
        assertEquals(SemanticAggregateFilterOperator.GT, filter.operator());
        assertEquals(100L, filter.value());
    }

    @Test
    void aggregate_filter_requires_a_collection_relationship_and_known_aggregate() {
        var model = new SemanticModelBuilder()
                .entity(new EntityId(1), "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .relationship(new RelationshipId(1), "Profile", new EntityId(2), RelationshipCardinality.ONE))
                .entity(new EntityId(2), "Profile", e -> e.identity(new FieldId(1), "Id"))
                .build();
        assertThrows(IllegalStateException.class,
                () -> new GraphQLSemanticAdapter(model).adapt("query { customer(where: { profile: { count: { eq: 1 } } }) { id } }"));
        assertThrows(IllegalStateException.class,
                () -> new GraphQLSemanticAdapter(aggregateModel())
                        .adapt("query { customer(where: { accounts: { balance: { avg: { gt: 1 } } } }) { id } }"));
    }

    // ----- CollectionOrderingTests ---------------------------------------------------------------------------------

    @Test
    void translates_count_and_max_collection_ordering() {
        var request = new GraphQLSemanticAdapter(aggregateModel()).adapt("""
                query { customer(order: { accounts: { _count: DESC, balance: { max: ASC } } }) { id } }
                """);
        var order = request.options().effectiveOrder();
        assertEquals(2, order.size());
        assertEquals(SemanticOrderAggregate.COUNT, order.get(0).aggregate());
        assertEquals(SemanticOrderAggregate.MAX, order.get(1).aggregate());
        assertEquals(List.of(new RelationshipId(401)), order.get(0).effectivePath());
    }

    // ----- RelationshipOrderingTests -------------------------------------------------------------------------------

    @Test
    void to_one_relationship_order_translates_to_a_path_aware_semantic_term() {
        var customerProfile = new RelationshipId(1);
        var model = new SemanticModelBuilder()
                .entity(new EntityId(1), "Customer", e -> e
                        .identity(new FieldId(1), "Id")
                        .relationship(customerProfile, "Profile", new EntityId(2), RelationshipCardinality.ONE))
                .entity(new EntityId(2), "Profile", e -> e
                        .identity(new FieldId(1), "Id")
                        .field(new FieldId(2), "DisplayName", String.class))
                .build();
        var request = new GraphQLSemanticAdapter(model).adapt("""
                query { customer(order: { profile: { displayName: DESC } }) { id profile { displayName } } }
                """);
        assertEquals(1, request.options().effectiveOrder().size());
        var term = request.options().effectiveOrder().get(0);
        assertEquals(new FieldId(2), term.field());
        assertEquals(SemanticSortDirection.DESC, term.direction());
        assertEquals(List.of(customerProfile), term.effectivePath());
    }

    @Test
    void collection_relationship_order_is_rejected_before_planning() {
        var ex = assertThrows(UnsupportedOperationException.class,
                () -> new GraphQLSemanticAdapter(aggregateModel())
                        .adapt("query { customer(order: { accounts: { balance: DESC } }) { id accounts { balance } } }"));
        assertTrue(ex.getMessage().toLowerCase().contains("collection relationship"));
    }

    // ----- GraphQLOperationTests (query half) ----------------------------------------------------------------------

    @Test
    void multiple_operations_require_an_operation_name() {
        var ex = assertThrows(IllegalStateException.class, () -> new GraphQLSemanticAdapter(customerOnly()).adapt("""
                query CustomerQuery { customer { id } }
                query OtherQuery { customer { name } }
                """));
        assertTrue(ex.getMessage().toLowerCase().contains("multiple operations"));
    }

    @Test
    void named_operation_is_selected() {
        var request = new GraphQLSemanticAdapter(customerOnly()).adapt("""
                query CustomerQuery { customer { id } }
                query OtherQuery { customer { name } }
                """, null, "OtherQuery");
        assertEquals(1, request.selections().size());
        assertEquals(new FieldId(2), request.selections().get(0).field());

        var viaTwoArgOverload = new GraphQLSemanticAdapter(customerOnly())
                .adapt("query A { customer { id } } query B { customer { name } }", "A");
        assertEquals(new FieldId(1), viaTwoArgOverload.selections().get(0).field());
    }

    @Test
    void unknown_operation_name_returns_validation_error() {
        var result = new GraphQLSemanticAdapter(customerOnly())
                .tryAdapt("query CustomerQuery { customer { id } }", null, "MissingQuery");
        assertFalse(result.succeeded());
        assertEquals(1, result.errors().size());
        assertEquals("GRAPHQL_VALIDATION_FAILED", result.error().code());
        assertTrue(result.error().message().contains("MissingQuery"));
    }

    @Test
    void single_operation_does_not_require_a_name() {
        var request = new GraphQLSemanticAdapter(customerOnly()).adapt("query CustomerQuery { customer { id } }");
        assertEquals(1, request.selections().size());
    }

    // ----- GraphQLErrorSemanticsTests (query half) -----------------------------------------------------------------

    @Test
    void try_adapt_returns_structured_validation_error() {
        var result = new GraphQLSemanticAdapter(customerOnly()).tryAdapt("query { customer { missing } }");
        assertFalse(result.succeeded());
        assertEquals(1, result.errors().size());
        assertEquals("GRAPHQL_VALIDATION_FAILED", result.error().code());
        assertTrue(result.error().message().contains("missing"));
        assertNull(result.data());
    }

    @Test
    void try_adapt_success_returns_data_and_no_errors() {
        var result = new GraphQLSemanticAdapter(customerOnly()).tryAdapt("query { customer { id name } }");
        assertTrue(result.succeeded());
        assertNotNull(result.data());
        assertTrue(result.errors().isEmpty());
    }

    @Test
    void variable_failures_are_reported_as_bad_user_input() {
        var result = new GraphQLSemanticAdapter(customerOnly()).tryAdapt(
                "query Q($f: Int) { customer(first: $f) { id } }", Map.of("f", "not-a-number"), "Q");
        assertFalse(result.succeeded());
        assertEquals("BAD_USER_INPUT", result.error().code());
    }

    @Test
    void existing_adapt_api_still_throws() {
        assertThrows(IllegalStateException.class,
                () -> new GraphQLSemanticAdapter(customerOnly()).adapt("query { customer { missing } }"));
        assertThrows(IllegalArgumentException.class, () -> new GraphQLSemanticAdapter(customerOnly()).adapt(" "));
    }

    // ----- AdapterBoundaryTests ------------------------------------------------------------------------------------

    @Test
    void query_adapter_does_not_reference_planning_execution_or_sql() throws Exception {
        byte[] bytes;
        try (var in = GraphQLSemanticAdapter.class.getResourceAsStream("GraphQLSemanticAdapter.class")) {
            bytes = in.readAllBytes();
        }
        var constantPool = new String(bytes, java.nio.charset.StandardCharsets.ISO_8859_1);
        assertFalse(constantPool.contains("com/foundgine/core/semantic/planning"));
        assertFalse(constantPool.contains("com/foundgine/core/execution"));
        assertFalse(constantPool.contains("com/foundgine/providers/storage/sql"));
    }
}
