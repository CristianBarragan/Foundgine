package com.foundgine.core.semantic.resolution;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.query.SemanticOrderAggregate;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticSortDirection;

import org.junit.jupiter.api.Test;

import java.util.List;

/** Port of Foundgine.Semantics.Tests.SemanticRequestResolverTests. */
class SemanticRequestResolverParityTest {
    @Test
    void requestResolvesToCustomerAccountTransactionGraph() {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var transaction = new EntityId(3);
        var model =
                new SemanticModelBuilder()
                        .entity(
                                customer,
                                "Customer",
                                e ->
                                        e.identity(new FieldId(1), "Id")
                                                .field(new FieldId(2), "Name", String.class)
                                                .relationship(
                                                        new RelationshipId(1),
                                                        "Accounts",
                                                        account,
                                                        RelationshipCardinality.MANY))
                        .entity(
                                account,
                                "Account",
                                e ->
                                        e.identity(new FieldId(1), "Id")
                                                .field(
                                                        new FieldId(3),
                                                        "Balance",
                                                        java.math.BigDecimal.class)
                                                .relationship(
                                                        new RelationshipId(2),
                                                        "Transactions",
                                                        transaction,
                                                        RelationshipCardinality.MANY))
                        .entity(
                                transaction,
                                "Transaction",
                                e ->
                                        e.identity(new FieldId(1), "Id")
                                                .field(
                                                        new FieldId(3),
                                                        "Amount",
                                                        java.math.BigDecimal.class))
                        .build();
        var txSelections =
                List.of(
                        new SemanticSelection(new FieldId(1), null, List.of()),
                        new SemanticSelection(new FieldId(3), null, List.of()));
        var accountSelections =
                List.of(
                        new SemanticSelection(new FieldId(1), null, List.of()),
                        new SemanticSelection(null, new RelationshipId(2), txSelections));
        var selections =
                List.of(
                        new SemanticSelection(new FieldId(1), null, List.of()),
                        new SemanticSelection(null, new RelationshipId(1), accountSelections));
        var request = new SemanticRequest(customer, selections, null, null);
        var graph =
                new SemanticRequestResolver(new SemanticContractSnapshot(model.freeze()))
                        .resolve(request);
        assertEquals(3, graph.nodes().size());
        assertEquals(customer, graph.nodes().get(0).entityId());
        assertEquals(List.of(new FieldId(1)), graph.nodes().get(0).fields());
        assertEquals(account, graph.nodes().get(1).entityId());
        assertEquals(new RelationshipId(1), graph.nodes().get(1).viaRelationship());
        assertEquals(0, graph.nodes().get(1).parentId());
        assertEquals(List.of(new FieldId(1)), graph.nodes().get(1).fields());
        assertEquals(transaction, graph.nodes().get(2).entityId());
        assertEquals(new RelationshipId(2), graph.nodes().get(2).viaRelationship());
        assertEquals(1, graph.nodes().get(2).parentId());
        assertEquals(List.of(new FieldId(1), new FieldId(3)), graph.nodes().get(2).fields());
    }

    @Test
    void requestCannotSelectUnknownRelationship() {
        var customer = new EntityId(1);
        var model =
                new SemanticModelBuilder()
                        .entity(customer, "Customer", e -> e.identity(new FieldId(1), "Id"))
                        .build();
        var request =
                new SemanticRequest(
                        customer,
                        List.of(new SemanticSelection(null, new RelationshipId(99), List.of())),
                        null,
                        null);
        var ex =
                assertThrows(
                        IllegalStateException.class,
                        () ->
                                new SemanticRequestResolver(
                                                new SemanticContractSnapshot(model.freeze()))
                                        .resolve(request));
        assertTrue(ex.getMessage().contains("does not declare relationship"));
    }

    /**
     * Port of C# {@code Foundgine.E2E.Tests.CollectionOrderingTests
     * .Min_requires_a_collection_path_and_target_field}. The C# original only asserts {@code
     * Assert.NotNull(resolved)} (i.e. that resolution accepts a well-formed MIN order term); no
     * Java test previously exercised {@code SemanticOrderAggregate.MIN} through the resolver at
     * all.
     */
    @Test
    void requestResolvesWithAMinCollectionOrderTerm() {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var accounts = new RelationshipId(1);
        var model =
                new SemanticModelBuilder()
                        .entity(
                                customer,
                                "Customer",
                                e ->
                                        e.identity(new FieldId(1), "Id")
                                                .relationship(
                                                        accounts,
                                                        "Accounts",
                                                        account,
                                                        RelationshipCardinality.MANY))
                        .entity(
                                account,
                                "Account",
                                e ->
                                        e.identity(new FieldId(1), "Id")
                                                .field(
                                                        new FieldId(3),
                                                        "Balance",
                                                        java.math.BigDecimal.class))
                        .build();

        var order =
                new SemanticOrderTerm(
                        new FieldId(3),
                        SemanticSortDirection.ASC,
                        List.of(accounts),
                        SemanticOrderAggregate.MIN);
        var options = new SemanticQueryOptions(null, List.of(order), null, null, null);
        var request =
                new SemanticRequest(
                        customer,
                        List.of(new SemanticSelection(new FieldId(1), null, List.of())),
                        options,
                        null);

        var graph =
                new SemanticRequestResolver(new SemanticContractSnapshot(model.freeze()))
                        .resolve(request);

        assertNotNull(graph);
        assertEquals(1, graph.nodes().size());
    }
}
