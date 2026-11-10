package com.foundgine.providers.storage.orm.jpa;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.providers.storage.orm.OrmAggregateCursorPipeline;
import com.foundgine.providers.storage.orm.OrmEntityMapping;
import com.foundgine.providers.storage.orm.OrmFetchRequest;
import com.foundgine.providers.storage.orm.OrmFieldBinding;
import com.foundgine.providers.storage.orm.OrmOrder;
import com.foundgine.providers.storage.orm.OrmPredicate;
import com.foundgine.providers.storage.orm.OrmPredicateValidator;
import com.foundgine.providers.storage.orm.OrmLink;
import com.foundgine.providers.storage.orm.InMemoryOrmQuerySource;

import java.math.BigDecimal;
import java.math.BigInteger;
import java.time.Instant;
import java.time.LocalDate;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.UUID;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Persistence;
import jakarta.persistence.Tuple;
import jakarta.persistence.criteria.CriteriaQuery;

import java.util.List;
import java.util.Map;

import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.parallel.Execution;
import org.junit.jupiter.api.parallel.ExecutionMode;

/** JPA-0/JPA-1: execute the real Criteria adapter against Hibernate ORM and H2/PostgreSQL. */
@Execution(ExecutionMode.SAME_THREAD)
class JpaOrmProviderIntegrationTest {
    private static EntityManagerFactory factory;
    private static final EntityId CUSTOMER = new EntityId(9100);
    private static final FieldId ID = new FieldId(9101);
    private static final FieldId NAME = new FieldId(9102);
    private static final FieldId REGION = new FieldId(9103);
    private static final FieldId ENABLED = new FieldId(9104);
    private static final OrmEntityMapping MAPPING = new OrmEntityMapping(
            CUSTOMER, "JpaBaselineCustomer", JpaBaselineCustomer.class,
            List.of(
                    new OrmFieldBinding(ID, "id", Long.class, true, new ColumnId(9101)),
                    new OrmFieldBinding(NAME, "name", String.class, false, new ColumnId(9102)),
                    new OrmFieldBinding(REGION, "region", String.class, false, new ColumnId(9103)),
                    new OrmFieldBinding(ENABLED, "enabled", Boolean.class, false, new ColumnId(9104))));
    private static final EntityId TYPE_FIXTURE = new EntityId(9200);
    private static final FieldId TYPE_ID = new FieldId(9201);
    private static final FieldId AMOUNT = new FieldId(9202);
    private static final FieldId QUANTITY = new FieldId(9203);
    private static final FieldId STATUS = new FieldId(9204);
    private static final FieldId EXTERNAL_ID = new FieldId(9205);
    private static final FieldId BUSINESS_DATE = new FieldId(9206);
    private static final FieldId CREATED_AT = new FieldId(9207);
    private static final FieldId OCCURRED_AT = new FieldId(9208);
    private static final OrmEntityMapping TYPE_MAPPING = new OrmEntityMapping(
            TYPE_FIXTURE, "JpaTypeFixture", JpaTypeFixture.class,
            List.of(
                    new OrmFieldBinding(TYPE_ID, "id", Long.class, true, new ColumnId(9201)),
                    new OrmFieldBinding(AMOUNT, "amount", BigDecimal.class, false, new ColumnId(9202)),
                    new OrmFieldBinding(QUANTITY, "quantity", BigInteger.class, false, new ColumnId(9203)),
                    new OrmFieldBinding(STATUS, "status", JpaTypeFixtureStatus.class, false, new ColumnId(9204)),
                    new OrmFieldBinding(EXTERNAL_ID, "externalId", UUID.class, false, new ColumnId(9205)),
                    new OrmFieldBinding(BUSINESS_DATE, "businessDate", LocalDate.class, false, new ColumnId(9206)),
                    new OrmFieldBinding(CREATED_AT, "createdAt", Instant.class, false, new ColumnId(9207)),
                    new OrmFieldBinding(OCCURRED_AT, "occurredAt", OffsetDateTime.class, false, new ColumnId(9208))));

    private static final EntityId ORDER_LINE = new EntityId(9300);
    private static final FieldId LINE_ID = new FieldId(9301);
    private static final FieldId LINE_CUSTOMER_ID = new FieldId(9302);
    private static final FieldId LINE_TENANT_ID = new FieldId(9303);
    private static final FieldId LINE_STATUS = new FieldId(9304);
    private static final OrmEntityMapping ORDER_LINE_MAPPING = new OrmEntityMapping(
            ORDER_LINE, "JpaBaselineOrderLine", JpaBaselineOrderLine.class,
            List.of(
                    new OrmFieldBinding(LINE_ID, "id", Long.class, true, new ColumnId(9301)),
                    new OrmFieldBinding(LINE_CUSTOMER_ID, "customerId", Long.class, false, new ColumnId(9302)),
                    new OrmFieldBinding(LINE_TENANT_ID, "tenantId", Long.class, false, new ColumnId(9303)),
                    new OrmFieldBinding(LINE_STATUS, "status", String.class, false, new ColumnId(9304))));

    @BeforeAll
    static void openFactory() {
        factory = Persistence.createEntityManagerFactory("foundgine-jpa-baseline");
    }

    @AfterAll
    static void closeFactory() {
        if (factory != null && factory.isOpen()) factory.close();
    }

    @Test
    void snapshotPageSessionOwnsOneActiveEntityManagerTransactionAndRollsBackOnClose() {
        var session = JpaOrmSnapshotPageSession.begin(
                factory, JpaOrmSnapshotPageSession.IsolationGuarantee.REPEATABLE_READ_OR_STRONGER);
        assertTrue(session.querySource() != null);
        session.close();
        assertThrows(IllegalStateException.class, session::querySource);
    }

    @BeforeEach
    void seed() {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            em.createQuery("delete from JpaBaselineOrderLine").executeUpdate();
            em.createQuery("delete from JpaBaselineCustomer").executeUpdate();
            em.persist(new JpaBaselineCustomer(1L, "Ann", "north"));
            em.persist(new JpaBaselineCustomer(2L, "Bob", "south"));
            em.persist(new JpaBaselineCustomer(3L, "Cy", "north"));
            em.persist(new JpaBaselineOrderLine(101L, 1L, 7L, "active"));
            em.persist(new JpaBaselineOrderLine(102L, 1L, 7L, "closed"));
            // A separate tenant-8 row supports cross-tenant cursor isolation without affecting
            // tenant-7 relationship quantifiers or aggregate ordering.
            em.persist(new JpaBaselineOrderLine(105L, 1L, 8L, "active"));
            em.persist(new JpaBaselineOrderLine(103L, 2L, 7L, "closed"));
            em.persist(new JpaBaselineOrderLine(104L, 3L, 7L, "active"));
            tx.commit();
        }
        JpaBaselineStatementInspector.clear();
    }

    @Test
    void executesCriteriaTupleProjectionWithBoundPredicateOrderingAndLimit() {
        var request = new OrmFetchRequest(
                MAPPING, List.of(ID, NAME),
                List.of(new OrmPredicate.Compare(REGION, OrmPredicate.Op.EQ, "north")),
                List.of(new OrmOrder(NAME, true)), 0, 1);

        var rows = new JpaOrmQuerySource(factory).fetch(request);

        assertEquals(1, rows.size());
        assertEquals(3L, rows.get(0).get(ID));
        assertEquals("Cy", rows.get(0).get(NAME));
        var sql = JpaBaselineStatementInspector.statements();
        assertTrue(sql.stream().anyMatch(statement -> statement.toLowerCase().contains("select")
                && statement.toLowerCase().contains("jpa_baseline_customer")));
        assertTrue(sql.stream().anyMatch(statement -> statement.contains("?")),
                "predicate values must be represented by JDBC bind placeholders");
        assertTrue(sql.stream().noneMatch(statement -> statement.contains("north")),
                "predicate values must not be interpolated into SQL text");
        assertFalse(JpaBaselineStatementInspector.statements().isEmpty());
    }

    @Test
    void aggregateOrderingUsesAuthorizedPrivateTupleSlotsAndNullsLast() {
        persistCustomer(new JpaBaselineCustomer(4L, "Dee", "west"));
        var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var order = new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), authorization);
        var request = new OrmFetchRequest(MAPPING, List.of(ID, NAME), List.of(),
                List.of(order, new OrmOrder(ID, false)), null, null);

        var fetched = new JpaOrmQuerySource(factory).fetchWithPrivateOrderValues(request);
        var rows = fetched.rows();

        assertEquals(List.of(1L, 3L, 2L, 4L), orderedIds(rows),
                "authorized MIN values sort ascending and empty relationships sort last");
        assertTrue(JpaBaselineStatementInspector.statements().stream()
                .anyMatch(sql -> sql.toLowerCase().contains("min")
                        || sql.toLowerCase().contains("least")),
                "aggregate selector must be translated by the database");
        assertTrue(JpaBaselineStatementInspector.statements().stream()
                .anyMatch(sql -> sql.toLowerCase().replace("_", "").contains("tenantid") && sql.contains("?")),
                "the aggregate subquery must apply child authorization through a bound parameter");
        assertTrue(rows.stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME))),
                "private aggregate tuple slots must never leak into the returned projection");
        assertEquals(java.util.Arrays.asList(List.of("active"), List.of("active"),
                        List.of("closed"), java.util.Arrays.asList((Object) null)),
                fetched.privateOrderValues(),
                "private aggregate values must stay aligned with semantic rows, including NULL");
    }

    @Test
    void fetchRequestCompilesAggregateContinuationAgainstPrivateOrderingExpressions() {
        persistCustomer(new JpaBaselineCustomer(4L, "Dee", "west"));
        var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var aggregate = new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), authorization);
        var request = new OrmFetchRequest(MAPPING, List.of(ID, NAME), List.of(),
                List.of(aggregate, new OrmOrder(ID, false)), null, 10,
                java.util.Arrays.asList("active", 1L));

        var fetched = new JpaOrmQuerySource(factory).fetchWithPrivateOrderValues(request);
        assertEquals(List.of(3L, 2L, 4L), orderedIds(fetched.rows()),
                "the continuation must advance through tied aggregates and then NULLS LAST");
        assertTrue(fetched.rows().stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME))),
                "continuation must not expose private aggregate selections");
        assertTrue(JpaBaselineStatementInspector.statements().stream()
                .anyMatch(sql -> sql.toLowerCase().contains("min") && sql.contains("?")),
                "aggregate continuation must use the same correlated expression and typed parameters");
    }

    @Test
    void h2SnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges() {
        persistCustomer(new JpaBaselineCustomer(4L, "Dee", "west"));
        var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var aggregate = new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), authorization);
        var order = List.of(aggregate, new OrmOrder(ID, false));
        var identity = "h2-snapshot-aggregate-cursor-v1-tenant-7";

        try (var session = JpaOrmSnapshotPageSession.begin(
                factory, JpaOrmSnapshotPageSession.IsolationGuarantee.REPEATABLE_READ_OR_STRONGER)) {
            var source = session.querySource();
            var first = source.fetchWithPrivateOrderValues(new OrmFetchRequest(
                    MAPPING, List.of(ID, NAME), List.of(), order, null, 2));
            assertEquals(List.of(1L, 3L), ids(first.rows()));
            var cursor = OrmAggregateCursorPipeline.nextPageCursor(identity, MAPPING, order,
                    first.privateOrderValues().get(1), first.rows().get(1));

            // A separate transaction commits after the snapshot has been established.
            try (EntityManager writer = factory.createEntityManager()) {
                var tx = writer.getTransaction();
                tx.begin();
                assertEquals(2, writer.createQuery(
                        "update JpaBaselineOrderLine l set l.status = 'zzz' where l.customerId = 1 and l.tenantId = 7")
                        .executeUpdate());
                assertEquals(1, writer.createQuery(
                        "update JpaBaselineOrderLine l set l.status = 'aaa' where l.customerId = 2 and l.tenantId = 7")
                        .executeUpdate());
                tx.commit();
            }

            var continuation = OrmAggregateCursorPipeline.continuationRequest(cursor, identity,
                    MAPPING, List.of(ID, NAME), List.of(), order, 10);
            var second = source.fetchWithPrivateOrderValues(continuation);
            assertEquals(List.of(2L, 4L), ids(second.rows()),
                    "repeatable-read page session must retain the original aggregate ordering snapshot");
            var all = new java.util.ArrayList<Long>();
            all.addAll(ids(first.rows()));
            all.addAll(ids(second.rows()));
            assertEquals(List.of(1L, 3L, 2L, 4L), all,
                    "snapshot pages must contain every original row exactly once despite concurrent child writes");
            assertTrue(second.rows().stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME))),
                    "private aggregate values must remain outside semantic projections");
            session.commit();
        }
    }

    @Test
    void aggregateCursorPagesHaveNoDuplicatesOrOmissionsAcrossTiesAndNulls() {
        persistCustomer(new JpaBaselineCustomer(4L, "Dee", "west"));
        var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var aggregate = new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), authorization);
        var order = List.of(aggregate, new OrmOrder(ID, false));
        var identity = "jpa-multipage-aggregate-min-customer-v1";
        var source = new JpaOrmQuerySource(factory);

        var first = source.fetchWithPrivateOrderValues(new OrmFetchRequest(
                MAPPING, List.of(ID, NAME), List.of(), order, null, 2));
        assertEquals(List.of(1L, 3L), ids(first.rows()));
        assertEquals(2, first.privateOrderValues().size());
        assertTrue(first.rows().stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME))),
                "private aggregate values must not leak into page projections");

        var firstCursor = OrmAggregateCursorPipeline.nextPageCursor(identity, MAPPING, order,
                first.privateOrderValues().get(1), first.rows().get(1));
        var secondRequest = OrmAggregateCursorPipeline.continuationRequest(firstCursor, identity,
                MAPPING, List.of(ID, NAME), List.of(), order, 2);
        var second = source.fetchWithPrivateOrderValues(secondRequest);
        assertEquals(List.of(2L, 4L), ids(second.rows()),
                "the next page must advance past equal MIN values, then include NULLS LAST");
        assertEquals(java.util.Arrays.asList(List.of("closed"), java.util.Arrays.asList((Object) null)),
                second.privateOrderValues());

        var secondCursor = OrmAggregateCursorPipeline.nextPageCursor(identity, MAPPING, order,
                second.privateOrderValues().get(1), second.rows().get(1));
        var thirdRequest = OrmAggregateCursorPipeline.continuationRequest(secondCursor, identity,
                MAPPING, List.of(ID, NAME), List.of(), order, 2);
        var third = source.fetchWithPrivateOrderValues(thirdRequest);
        assertTrue(third.rows().isEmpty(), "a cursor after the final NULLS LAST row must be exhausted");

        var allIds = new java.util.ArrayList<Long>();
        allIds.addAll(ids(first.rows()));
        allIds.addAll(ids(second.rows()));
        allIds.addAll(ids(third.rows()));
        assertEquals(List.of(1L, 3L, 2L, 4L), allIds,
                "concatenated pages must equal the complete deterministic ordering exactly once");
    }

    @Test
    void aggregateCursorCannotBeReplayedAcrossTenantAuthorization() {
        var tenantSeven = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var tenantEight = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 8L);
        var orderSeven = List.of(new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), tenantSeven),
                new OrmOrder(ID, false));
        var orderEight = List.of(new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), tenantEight),
                new OrmOrder(ID, false));
        var identitySeven = "jpa-customer-v1|effective-child-authorization=tenant:7";
        var identityEight = "jpa-customer-v1|effective-child-authorization=tenant:8";
        var source = new JpaOrmQuerySource(factory);
        var first = source.fetchWithPrivateOrderValues(new OrmFetchRequest(
                MAPPING, List.of(ID, NAME), List.of(), orderSeven, null, 1));
        assertEquals(List.of(1L), ids(first.rows()));
        var cursor = OrmAggregateCursorPipeline.nextPageCursor(identitySeven, MAPPING, orderSeven,
                first.privateOrderValues().get(0), first.rows().get(0));

        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorPipeline.continuationRequest(cursor, identityEight,
                        MAPPING, List.of(ID, NAME), List.of(), orderEight, 1),
                "a tenant-7 cursor must not decode under tenant-8 effective authorization");
        assertEquals(List.of(1L), ids(source.fetchWithPrivateOrderValues(new OrmFetchRequest(
                MAPPING, List.of(ID), List.of(), orderEight, null, 1)).rows()),
                "tenant 8 has a separately authorized child row, but cannot reuse tenant 7's cursor");
    }

    @Test
    void aggregateCursorIsRejectedWhenChildAuthorizationChangesWithinTenant() {
        var tenantSeven = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var activeOnly = new OrmPredicate.And(List.of(tenantSeven,
                new OrmPredicate.Compare(LINE_STATUS, OrmPredicate.Op.EQ, "active")));
        var originalOrder = List.of(new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), tenantSeven),
                new OrmOrder(ID, false));
        var changedOrder = List.of(new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), activeOnly),
                new OrmOrder(ID, false));
        var originalIdentity = "jpa-customer-v1|tenant:7|child-auth:all-visible";
        var changedIdentity = "jpa-customer-v1|tenant:7|child-auth:active-only";
        var first = new JpaOrmQuerySource(factory).fetchWithPrivateOrderValues(new OrmFetchRequest(
                MAPPING, List.of(ID, NAME), List.of(), originalOrder, null, 1));
        var cursor = OrmAggregateCursorPipeline.nextPageCursor(originalIdentity, MAPPING, originalOrder,
                first.privateOrderValues().get(0), first.rows().get(0));

        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorPipeline.continuationRequest(cursor, changedIdentity,
                        MAPPING, List.of(ID, NAME), List.of(), changedOrder, 1),
                "changing child visibility must invalidate the previous aggregate cursor");
    }

    @Test
    void criteriaContinuationCompilesTypedLexicographicPredicateWithBoundValues() {
        persistCustomer(new JpaBaselineCustomer(4L, "Dee", "west"));
        try (EntityManager em = factory.createEntityManager()) {
            var cb = em.getCriteriaBuilder();
            CriteriaQuery<Tuple> criteria = cb.createTupleQuery();
            var root = criteria.from(JpaBaselineCustomer.class);
            var name = root.get("name");
            var id = root.get("id");
            var bindings = new JpaParameterBindings();
            var continuation = JpaAggregateContinuation.compile(cb,
                    List.of(name, id), List.of("Bob", 2L), List.of(false, false), bindings);
            criteria.multiselect(id.alias("id"), name.alias("name"))
                    .where(continuation).orderBy(cb.asc(name), cb.asc(id));
            var query = em.createQuery(criteria);
            bindings.bind(query);
            var rows = query.getResultList();
            assertEquals(List.of("Cy", "Dee"), rows.stream().map(row -> row.get("name")).toList());
            assertTrue(JpaBaselineStatementInspector.statements().stream()
                    .anyMatch(sql -> sql.contains("?")),
                    "continuation values must be typed JDBC parameters, not SQL literals");
        }
    }

    @Test
    void inMemoryAggregateOrderingMatchesAuthorizedMinAndNullPolicy() {
        var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var order = new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), authorization);
        var source = new InMemoryOrmQuerySource()
                .add(CUSTOMER, Map.of(ID, 1L, NAME, "Ann"))
                .add(CUSTOMER, Map.of(ID, 2L, NAME, "Bob"))
                .add(CUSTOMER, Map.of(ID, 3L, NAME, "Cy"))
                .add(CUSTOMER, Map.of(ID, 4L, NAME, "Dee"))
                .add(ORDER_LINE, Map.of(LINE_ID, 101L, LINE_CUSTOMER_ID, 1L, LINE_TENANT_ID, 7L, LINE_STATUS, "active"))
                .add(ORDER_LINE, Map.of(LINE_ID, 102L, LINE_CUSTOMER_ID, 1L, LINE_TENANT_ID, 8L, LINE_STATUS, "archived"))
                .add(ORDER_LINE, Map.of(LINE_ID, 103L, LINE_CUSTOMER_ID, 2L, LINE_TENANT_ID, 7L, LINE_STATUS, "closed"))
                .add(ORDER_LINE, Map.of(LINE_ID, 104L, LINE_CUSTOMER_ID, 3L, LINE_TENANT_ID, 7L, LINE_STATUS, "active"));
        var request = new OrmFetchRequest(MAPPING, List.of(ID), List.of(), List.of(order, new OrmOrder(ID, false)), null, null);
        assertEquals(List.of(1L, 3L, 2L, 4L), orderedIds(source.fetch(request)));
    }

    @Test
    void correlatedExistsAppliesChildAuthorizationAndBindsNestedParameters() {
        // Customer 1's active child is deliberately moved outside tenant 7 for this probe.
        updateOrderLineTenant(101L, 8L);
        var childAuthorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var childFilter = new OrmPredicate.Compare(LINE_STATUS, OrmPredicate.Op.EQ, "active");
        var related = new OrmPredicate.Exists(ORDER_LINE_MAPPING,
                new OrmLink(ID, LINE_CUSTOMER_ID), childAuthorization, childFilter);
        var rows = new JpaOrmQuerySource(factory).fetch(request(related));
        assertEquals(List.of(3L), ids(rows),
                "a child from another tenant must not authorize its parent");
        var sql = JpaBaselineStatementInspector.statements();
        assertTrue(sql.stream().anyMatch(statement -> statement.toLowerCase().contains("exists")));
        assertTrue(sql.stream().anyMatch(statement -> statement.contains("?")));
        assertTrue(sql.stream().noneMatch(statement -> statement.contains("active")));
    }

    @Test
    void inMemoryOracleUsesTheSameCorrelatedChildAuthorizationPredicate() {
        var childAuthorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var childWhere = new OrmPredicate.Compare(LINE_STATUS, OrmPredicate.Op.EQ, "active");
        var exists = new OrmPredicate.Exists(ORDER_LINE_MAPPING,
                new OrmLink(ID, LINE_CUSTOMER_ID), childAuthorization, childWhere);
        var source = new InMemoryOrmQuerySource()
                .add(CUSTOMER, Map.of(ID, 1L))
                .add(CUSTOMER, Map.of(ID, 3L))
                .add(ORDER_LINE, Map.of(LINE_ID, 101L, LINE_CUSTOMER_ID, 1L, LINE_TENANT_ID, 8L, LINE_STATUS, "active"))
                .add(ORDER_LINE, Map.of(LINE_ID, 104L, LINE_CUSTOMER_ID, 3L, LINE_TENANT_ID, 7L, LINE_STATUS, "active"));
        var request = new OrmFetchRequest(MAPPING, List.of(ID), List.of(exists), List.of(), null, null);
        assertEquals(List.of(3L), source.fetch(request).stream().map(row -> (Long) row.get(ID)).toList());
    }

    @Test
    void relationshipSomeNoneAndAllUseAuthorizationVisibleChildrenOnly() {
        persistCustomer(new JpaBaselineCustomer(4L, "Dee", "west"));
        var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var active = new OrmPredicate.Compare(LINE_STATUS, OrmPredicate.Op.EQ, "active");
        var source = new JpaOrmQuerySource(factory);

        var some = new OrmPredicate.Quantified(OrmPredicate.Quantifier.SOME, ORDER_LINE_MAPPING,
                new OrmLink(ID, LINE_CUSTOMER_ID), authorization, active);
        var none = new OrmPredicate.Quantified(OrmPredicate.Quantifier.NONE, ORDER_LINE_MAPPING,
                new OrmLink(ID, LINE_CUSTOMER_ID), authorization, active);
        var all = new OrmPredicate.Quantified(OrmPredicate.Quantifier.ALL, ORDER_LINE_MAPPING,
                new OrmLink(ID, LINE_CUSTOMER_ID), authorization, active);

        assertEquals(List.of(1L, 3L), ids(source.fetch(request(some))));
        assertEquals(List.of(2L, 4L), ids(source.fetch(request(none))));
        assertEquals(List.of(3L, 4L), ids(source.fetch(request(all))),
                "ALL is vacuously true when no authorization-visible child exists");
    }

    @Test
    void inMemoryRelationshipQuantifiersMatchJpaEmptySetAndAuthorizationSemantics() {
        var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var active = new OrmPredicate.Compare(LINE_STATUS, OrmPredicate.Op.EQ, "active");
        var source = new InMemoryOrmQuerySource()
                .add(CUSTOMER, Map.of(ID, 1L)).add(CUSTOMER, Map.of(ID, 2L))
                .add(CUSTOMER, Map.of(ID, 3L)).add(CUSTOMER, Map.of(ID, 4L))
                .add(ORDER_LINE, Map.of(LINE_ID, 101L, LINE_CUSTOMER_ID, 1L, LINE_TENANT_ID, 7L, LINE_STATUS, "active"))
                .add(ORDER_LINE, Map.of(LINE_ID, 102L, LINE_CUSTOMER_ID, 1L, LINE_TENANT_ID, 7L, LINE_STATUS, "closed"))
                .add(ORDER_LINE, Map.of(LINE_ID, 103L, LINE_CUSTOMER_ID, 2L, LINE_TENANT_ID, 7L, LINE_STATUS, "closed"))
                .add(ORDER_LINE, Map.of(LINE_ID, 104L, LINE_CUSTOMER_ID, 3L, LINE_TENANT_ID, 7L, LINE_STATUS, "active"));
        for (var quantifier : OrmPredicate.Quantifier.values()) {
            var predicate = new OrmPredicate.Quantified(quantifier, ORDER_LINE_MAPPING,
                    new OrmLink(ID, LINE_CUSTOMER_ID), authorization, active);
            var request = new OrmFetchRequest(MAPPING, List.of(ID), List.of(predicate), List.of(), null, null);
            var expected = switch (quantifier) {
                case SOME -> List.of(1L, 3L);
                case NONE -> List.of(2L, 4L);
                case ALL -> List.of(3L, 4L);
            };
            assertEquals(expected, ids(source.fetch(request)), quantifier.name());
        }
    }

    @Test
    void authorizedRelationshipCountMinAndMaxFiltersMatchInMemorySemantics() {
        var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
        var allChildren = new OrmPredicate.Const(true);
        var link = new OrmLink(ID, LINE_CUSTOMER_ID);
        var count = new OrmPredicate.Aggregate(OrmPredicate.AggregateKind.COUNT, ORDER_LINE_MAPPING,
                link, authorization, allChildren, null, OrmPredicate.Op.GTE, 1L);
        var min = new OrmPredicate.Aggregate(OrmPredicate.AggregateKind.MIN, ORDER_LINE_MAPPING,
                link, authorization, allChildren, LINE_STATUS, OrmPredicate.Op.EQ, "active");
        var max = new OrmPredicate.Aggregate(OrmPredicate.AggregateKind.MAX, ORDER_LINE_MAPPING,
                link, authorization, allChildren, LINE_STATUS, OrmPredicate.Op.EQ, "closed");
        // Hide child 102 from tenant 7 so the JPA fixture matches the in-memory one below.
        updateOrderLineTenant(102L, 8L);
        var jpa = new JpaOrmQuerySource(factory);
        assertEquals(List.of(1L, 2L, 3L), ids(jpa.fetch(request(count))));
        assertEquals(List.of(1L, 3L), ids(jpa.fetch(request(min))));
        assertEquals(List.of(2L), ids(jpa.fetch(request(max))));

        var memory = new InMemoryOrmQuerySource()
                .add(CUSTOMER, Map.of(ID, 1L)).add(CUSTOMER, Map.of(ID, 2L))
                .add(CUSTOMER, Map.of(ID, 3L)).add(CUSTOMER, Map.of(ID, 4L))
                .add(ORDER_LINE, Map.of(LINE_ID, 101L, LINE_CUSTOMER_ID, 1L, LINE_TENANT_ID, 7L, LINE_STATUS, "active"))
                .add(ORDER_LINE, Map.of(LINE_ID, 102L, LINE_CUSTOMER_ID, 1L, LINE_TENANT_ID, 8L, LINE_STATUS, "closed"))
                .add(ORDER_LINE, Map.of(LINE_ID, 103L, LINE_CUSTOMER_ID, 2L, LINE_TENANT_ID, 7L, LINE_STATUS, "closed"))
                .add(ORDER_LINE, Map.of(LINE_ID, 104L, LINE_CUSTOMER_ID, 3L, LINE_TENANT_ID, 7L, LINE_STATUS, "active"));
        assertEquals(List.of(1L, 2L, 3L), ids(memory.fetch(request(count))));
        assertEquals(List.of(1L, 3L), ids(memory.fetch(request(min))));
        assertEquals(List.of(2L), ids(memory.fetch(request(max))));
    }

    @Test
    void nestedRelationshipTraversalIsBoundedAndValidatedBeforeProviderExecution() {
        OrmPredicate nested = new OrmPredicate.Compare(LINE_STATUS, OrmPredicate.Op.EQ, "active");
        for (int i = 0; i < OrmPredicateValidator.MAX_RELATIONSHIP_DEPTH; i++) {
            nested = new OrmPredicate.Quantified(OrmPredicate.Quantifier.SOME, ORDER_LINE_MAPPING,
                    new OrmLink(LINE_CUSTOMER_ID, LINE_CUSTOMER_ID), new OrmPredicate.Const(true), nested);
        }
        OrmPredicateValidator.validate(nested);

        OrmPredicate tooDeep = new OrmPredicate.Quantified(OrmPredicate.Quantifier.SOME, ORDER_LINE_MAPPING,
                new OrmLink(LINE_CUSTOMER_ID, LINE_CUSTOMER_ID), new OrmPredicate.Const(true), nested);
        var request = new OrmFetchRequest(MAPPING, List.of(ID), List.of(tooDeep), List.of(), null, null);
        assertThrows(IllegalArgumentException.class, () -> new JpaOrmQuerySource(factory).fetch(request));
        assertThrows(IllegalArgumentException.class, () -> OrmPredicateValidator.validate(tooDeep));
    }

    @Test
    void predicateValidatorRejectsOversizedBooleanTrees() {
        var terms = new java.util.ArrayList<OrmPredicate>();
        for (int i = 0; i <= OrmPredicateValidator.MAX_NODES; i++) {
            terms.add(new OrmPredicate.Const(true));
        }
        assertThrows(IllegalArgumentException.class,
                () -> OrmPredicateValidator.validate(new OrmPredicate.And(terms)));
    }

    @Test
    void callerManagedEntityManagerRemainsOpenAfterFetch() {
        try (EntityManager em = factory.createEntityManager()) {
            var source = new JpaOrmQuerySource(em);
            var request = new OrmFetchRequest(MAPPING, List.of(ID), List.of(), List.of(), null, null);
            assertEquals(3, source.fetch(request).size());
            assertTrue(em.isOpen(), "caller-owned EntityManager must not be closed by the adapter");
        }
    }

    @Test
    void factoryManagedFetchDoesNotLeakEntityManagers() {
        var request = new OrmFetchRequest(MAPPING, List.of(ID), List.of(), List.of(), null, null);
        assertEquals(3, new JpaOrmQuerySource(factory).fetch(request).size());
        assertTrue(factory.isOpen(), "short-lived fetch must not close the shared factory");
    }

    @Test
    void emptyAndNullOnlyInListsAreFalseAndMixedListsIgnoreNullItems() {
        var source = new JpaOrmQuerySource(factory);
        assertEquals(0, source.fetch(request(new OrmPredicate.In(REGION, List.of()))).size());
        assertEquals(0, source.fetch(request(new OrmPredicate.In(REGION, java.util.Arrays.asList((Object) null)))).size());
        assertEquals(2, source.fetch(request(new OrmPredicate.In(
                REGION, java.util.Arrays.<Object>asList("north", null)))).size());
    }

    @Test
    void explicitNullComparisonsUseIsNullAndIsNotNullSemantics() {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            em.persist(new JpaBaselineCustomer(4L, "Null region", null));
            tx.commit();
        }
        var source = new JpaOrmQuerySource(factory);
        assertEquals(1, source.fetch(request(new OrmPredicate.Compare(
                REGION, OrmPredicate.Op.EQ, null))).size());
        assertEquals(3, source.fetch(request(new OrmPredicate.Compare(
                REGION, OrmPredicate.Op.NEQ, null))).size());
    }

    @Test
    void notInUsesTwoValuedComplementForNullFieldsAndNullMembers() {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            em.persist(new JpaBaselineCustomer(4L, "Null region", null));
            tx.commit();
        }
        var notIn = new OrmPredicate.Not(new OrmPredicate.In(
                REGION, java.util.Arrays.<Object>asList("north", null)));
        assertEquals(List.of(2L, 4L), ids(new JpaOrmQuerySource(factory).fetch(request(notIn))));
    }

    @Test
    void negatedScalarComparisonsUseBooleanComplementForNullFields() {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            em.persist(new JpaBaselineCustomer(4L, "Null region", null));
            tx.commit();
        }
        var source = new JpaOrmQuerySource(factory);
        var equalNorth = new OrmPredicate.Compare(REGION, OrmPredicate.Op.EQ, "north");
        var notEqualNorth = new OrmPredicate.Compare(REGION, OrmPredicate.Op.NEQ, "north");
        assertEquals(List.of(2L, 4L), ids(source.fetch(request(new OrmPredicate.Not(equalNorth)))));
        assertEquals(List.of(1L, 3L, 4L), ids(source.fetch(request(new OrmPredicate.Not(notEqualNorth)))));
    }

    @Test
    void allScalarComparisonOperatorsExecuteWithTypedValues() {
        var source = new JpaOrmQuerySource(factory);
        assertEquals(List.of(2L, 3L), ids(source.fetch(request(new OrmPredicate.Compare(ID, OrmPredicate.Op.GT, 1L)))));
        assertEquals(List.of(2L, 3L), ids(source.fetch(request(new OrmPredicate.Compare(ID, OrmPredicate.Op.GTE, 2L)))));
        assertEquals(List.of(1L, 2L), ids(source.fetch(request(new OrmPredicate.Compare(ID, OrmPredicate.Op.LT, 3L)))));
        assertEquals(List.of(1L, 2L), ids(source.fetch(request(new OrmPredicate.Compare(ID, OrmPredicate.Op.LTE, 2L)))));
        assertEquals(List.of(2L), ids(source.fetch(request(new OrmPredicate.Compare(ID, OrmPredicate.Op.EQ, 2L)))));
        assertEquals(List.of(1L, 3L), ids(source.fetch(request(new OrmPredicate.Compare(ID, OrmPredicate.Op.NEQ, 2L)))));
    }

    @Test
    void comparisonAndEveryInMemberUseExplicitTypedCriteriaParameters() {
        var predicate = new OrmPredicate.And(List.of(
                new OrmPredicate.Compare(REGION, OrmPredicate.Op.EQ, "north"),
                new OrmPredicate.In(ID, List.of(1L, 3L))));
        var rows = new JpaOrmQuerySource(factory).fetch(request(predicate));
        assertEquals(List.of(1L, 3L), ids(rows));
        var sql = JpaBaselineStatementInspector.statements();
        assertTrue(sql.stream().anyMatch(statement -> statement.contains("?")),
                "comparison and IN values must be represented by bind placeholders");
        assertTrue(sql.stream().noneMatch(statement -> statement.contains("north")),
                "string values must not be interpolated into SQL");
        assertTrue(sql.stream().noneMatch(statement -> statement.matches(".*\\b[13]\\b.*")),
                "numeric values must not be interpolated into SQL");
    }

    @Test
    void mutationFindBindsPredicateParametersExplicitly() {
        try (EntityManager em = factory.createEntityManager()) {
            var found = new JpaOrmMutationSource(em).find(MAPPING,
                    new OrmPredicate.Compare(REGION, OrmPredicate.Op.EQ, "south"));
            assertEquals(1, found.size());
            assertEquals(2L, ((JpaBaselineCustomer) found.get(0)).getId());
            var sql = JpaBaselineStatementInspector.statements();
            assertTrue(sql.stream().anyMatch(statement -> statement.contains("?")));
            assertTrue(sql.stream().noneMatch(statement -> statement.contains("south")));
        }
    }

    @Test
    void nestedAndOrNotAndConstantPredicatesComposeCorrectly() {
        var source = new JpaOrmQuerySource(factory);
        var north = new OrmPredicate.Compare(REGION, OrmPredicate.Op.EQ, "north");
        var idTwo = new OrmPredicate.Compare(ID, OrmPredicate.Op.EQ, 2L);
        var idGreaterThanOne = new OrmPredicate.Compare(ID, OrmPredicate.Op.GT, 1L);
        assertEquals(List.of(3L), ids(source.fetch(request(new OrmPredicate.And(List.of(north, idGreaterThanOne))))));
        assertEquals(List.of(1L, 2L, 3L), ids(source.fetch(request(new OrmPredicate.Or(List.of(north, idTwo))))));
        assertEquals(List.of(2L), ids(source.fetch(request(new OrmPredicate.Not(north)))));
        assertEquals(3, source.fetch(request(new OrmPredicate.Not(
                new OrmPredicate.In(REGION, List.of())))).size());
        assertEquals(3, source.fetch(request(new OrmPredicate.Const(true))).size());
        assertEquals(0, source.fetch(request(new OrmPredicate.Const(false))).size());
    }

    @Test
    void mismatchedAndNonOrderableValuesFailClosedBeforeCriteriaPathResolution() {
        var source = new JpaOrmQuerySource(factory);
        var mismatch = assertThrows(IllegalArgumentException.class,
                () -> source.fetch(request(new OrmPredicate.Compare(ID, OrmPredicate.Op.EQ, "not-a-long"))));
        assertTrue(mismatch.getMessage().contains("must be java.lang.Long"));
        assertThrows(IllegalArgumentException.class,
                () -> source.fetch(request(new OrmPredicate.In(ID, List.of("not-a-long")))));
        assertThrows(IllegalArgumentException.class,
                () -> source.fetch(request(new OrmPredicate.Compare(ENABLED, OrmPredicate.Op.GT, true))));
    }


    @Test
    void decimalAndBigIntegerValuesRetainExactPrecisionThroughTypedParameters() {
        BigDecimal amount = new BigDecimal("123456789012345.678901234567");
        BigInteger quantity = new BigInteger("987654321098765432109876543210");
        persistTypeFixture(amount, quantity, JpaTypeFixtureStatus.READY,
                UUID.fromString("123e4567-e89b-12d3-a456-426614174000"),
                LocalDate.of(2026, 11, 10), Instant.parse("2026-11-10T00:00:00Z"),
                OffsetDateTime.of(2026, 11, 10, 12, 30, 0, 0, ZoneOffset.UTC));
        var source = new JpaOrmQuerySource(factory);
        assertEquals(List.of(1L), typeIds(source.fetch(typeRequest(
                new OrmPredicate.Compare(AMOUNT, OrmPredicate.Op.EQ, amount)))));
        assertEquals(List.of(1L), typeIds(source.fetch(typeRequest(
                new OrmPredicate.Compare(QUANTITY, OrmPredicate.Op.GT,
                        new BigInteger("987654321098765432109876543000"))))));
        assertEquals(0, source.fetch(typeRequest(new OrmPredicate.Compare(
                AMOUNT, OrmPredicate.Op.EQ, new BigDecimal("123456789012345.678901234568")))).size());
    }

    @Test
    void enumAndUuidEqualityUseTypedParametersWithoutAssumingOrdering() {
        UUID externalId = UUID.fromString("123e4567-e89b-12d3-a456-426614174000");
        persistTypeFixture(new BigDecimal("1.25"), BigInteger.TEN, JpaTypeFixtureStatus.READY,
                externalId, LocalDate.of(2026, 11, 10), Instant.parse("2026-11-10T00:00:00Z"),
                OffsetDateTime.of(2026, 11, 10, 12, 30, 0, 0, ZoneOffset.UTC));
        var source = new JpaOrmQuerySource(factory);
        assertEquals(List.of(1L), typeIds(source.fetch(typeRequest(
                new OrmPredicate.Compare(STATUS, OrmPredicate.Op.EQ, JpaTypeFixtureStatus.READY)))));
        assertEquals(List.of(1L), typeIds(source.fetch(typeRequest(
                new OrmPredicate.In(EXTERNAL_ID, List.of(externalId))))));
        assertThrows(IllegalArgumentException.class, () -> source.fetch(typeRequest(
                new OrmPredicate.Compare(STATUS, OrmPredicate.Op.GT, JpaTypeFixtureStatus.READY))));
        assertThrows(IllegalArgumentException.class, () -> source.fetch(typeRequest(
                new OrmPredicate.Compare(EXTERNAL_ID, OrmPredicate.Op.LT, externalId))));
    }

    @Test
    void javaTimeTypesSupportEqualityAndDeclaredChronologicalOrdering() {
        LocalDate date = LocalDate.of(2026, 11, 10);
        Instant instant = Instant.parse("2026-11-10T00:00:00Z");
        OffsetDateTime offset = OffsetDateTime.of(2026, 11, 10, 12, 30, 0, 0, ZoneOffset.UTC);
        persistTypeFixture(new BigDecimal("1.25"), BigInteger.TEN, JpaTypeFixtureStatus.READY,
                UUID.randomUUID(), date, instant, offset);
        var source = new JpaOrmQuerySource(factory);
        assertEquals(List.of(1L), typeIds(source.fetch(typeRequest(
                new OrmPredicate.Compare(BUSINESS_DATE, OrmPredicate.Op.EQ, date)))));
        assertEquals(List.of(1L), typeIds(source.fetch(typeRequest(
                new OrmPredicate.Compare(CREATED_AT, OrmPredicate.Op.GTE, instant)))));
        assertEquals(List.of(1L), typeIds(source.fetch(typeRequest(
                new OrmPredicate.Compare(OCCURRED_AT, OrmPredicate.Op.LTE, offset)))));
    }

    private static void persistTypeFixture(BigDecimal amount, BigInteger quantity, JpaTypeFixtureStatus status,
            UUID externalId, LocalDate businessDate, Instant createdAt, OffsetDateTime occurredAt) {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            em.createQuery("delete from JpaTypeFixture").executeUpdate();
            em.persist(new JpaTypeFixture(1L, amount, quantity, status, externalId, businessDate, createdAt, occurredAt));
            tx.commit();
        }
    }

    private static OrmFetchRequest typeRequest(OrmPredicate predicate) {
        return new OrmFetchRequest(TYPE_MAPPING, List.of(TYPE_ID), List.of(predicate), List.of(), null, null);
    }

    private static List<Long> typeIds(List<Map<FieldId, Object>> rows) {
        return rows.stream().map(row -> (Long) row.get(TYPE_ID)).toList();
    }

    private static void persistCustomer(JpaBaselineCustomer customer) {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            em.persist(customer);
            tx.commit();
        }
    }

    private static void updateOrderLineTenant(long lineId, long tenantId) {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            int updated = em.createQuery(
                    "update JpaBaselineOrderLine l set l.tenantId = :tenant where l.id = :id")
                    .setParameter("tenant", tenantId)
                    .setParameter("id", lineId)
                    .executeUpdate();
            if (updated != 1) {
                tx.rollback();
                throw new IllegalStateException("Expected to update exactly one order-line fixture.");
            }
            tx.commit();
        }
    }

    private static OrmFetchRequest request(OrmPredicate predicate) {
        return new OrmFetchRequest(MAPPING, List.of(ID), List.of(predicate), List.of(), null, null);
    }

    private static List<Long> ids(List<Map<FieldId, Object>> rows) {
        return rows.stream().map(row -> (Long) row.get(ID)).sorted().toList();
    }

    private static List<Long> orderedIds(List<Map<FieldId, Object>> rows) {
        return rows.stream().map(row -> (Long) row.get(ID)).toList();
    }

    @Test
    void postgresExecutesTheSameScalarCriteriaContractWhenConfigured() {
        String url = System.getenv("FOUNDGINE_JPA_POSTGRES_URL");
        assumeTrue(url != null && !url.isBlank(), "PostgreSQL integration service is not configured");
        var properties = new java.util.HashMap<String, Object>();
        properties.put("jakarta.persistence.jdbc.driver", "org.postgresql.Driver");
        properties.put("jakarta.persistence.jdbc.url", url);
        properties.put("jakarta.persistence.jdbc.user", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_USER", "postgres"));
        properties.put("jakarta.persistence.jdbc.password", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_PASSWORD", "postgres"));
        try (var postgresFactory = Persistence.createEntityManagerFactory("foundgine-jpa-postgres", properties)) {
            try (EntityManager em = postgresFactory.createEntityManager()) {
                var tx = em.getTransaction();
                tx.begin();
                em.createQuery("delete from JpaBaselineOrderLine").executeUpdate();
            em.createQuery("delete from JpaBaselineCustomer").executeUpdate();
                em.persist(new JpaBaselineCustomer(1L, "Ann", "north"));
                em.persist(new JpaBaselineCustomer(2L, "Bob", "south"));
                tx.commit();
            }
            var request = new OrmFetchRequest(MAPPING, List.of(ID, NAME),
                    List.of(new OrmPredicate.Compare(REGION, OrmPredicate.Op.EQ, "north")),
                    List.of(new OrmOrder(NAME, false)), null, null);
            var source = new JpaOrmQuerySource(postgresFactory);
            var rows = source.fetch(request);
            assertEquals(1, rows.size());
            assertEquals(1L, rows.get(0).get(ID));
            assertEquals("Ann", rows.get(0).get(NAME));
            assertEquals(2, source.fetch(request(new OrmPredicate.In(REGION, List.of("north", "south")))).size());
            assertEquals(0, source.fetch(request(new OrmPredicate.In(REGION, List.of()))).size());
            assertEquals(List.of(2L), ids(source.fetch(request(
                    new OrmPredicate.Compare(ID, OrmPredicate.Op.GT, 1L)))));
            assertEquals(List.of(1L, 2L), ids(source.fetch(request(
                    new OrmPredicate.Or(List.of(
                            new OrmPredicate.Compare(REGION, OrmPredicate.Op.EQ, "north"),
                            new OrmPredicate.Compare(ID, OrmPredicate.Op.EQ, 2L)))))));
            try (EntityManager em = postgresFactory.createEntityManager()) {
                var tx = em.getTransaction();
                tx.begin();
                em.persist(new JpaBaselineCustomer(3L, "Null region", null));
                tx.commit();
            }
            assertEquals(1, source.fetch(request(new OrmPredicate.Compare(
                    REGION, OrmPredicate.Op.EQ, null))).size());
        }
    }

    @Test
    void postgresExecutesAggregateCursorPagesWithoutDuplicatesOrOmissionsWhenConfigured() {
        String url = System.getenv("FOUNDGINE_JPA_POSTGRES_URL");
        assumeTrue(url != null && !url.isBlank(), "PostgreSQL integration service is not configured");
        var properties = new java.util.HashMap<String, Object>();
        properties.put("jakarta.persistence.jdbc.driver", "org.postgresql.Driver");
        properties.put("jakarta.persistence.jdbc.url", url);
        properties.put("jakarta.persistence.jdbc.user", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_USER", "postgres"));
        properties.put("jakarta.persistence.jdbc.password", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_PASSWORD", "postgres"));

        try (var postgresFactory = Persistence.createEntityManagerFactory("foundgine-jpa-postgres", properties)) {
            try (EntityManager em = postgresFactory.createEntityManager()) {
                var tx = em.getTransaction();
                tx.begin();
                em.createQuery("delete from JpaBaselineOrderLine").executeUpdate();
                em.createQuery("delete from JpaBaselineCustomer").executeUpdate();
                em.persist(new JpaBaselineCustomer(1L, "Ann", "north"));
                em.persist(new JpaBaselineCustomer(2L, "Bob", "south"));
                em.persist(new JpaBaselineCustomer(3L, "Cy", "north"));
                em.persist(new JpaBaselineCustomer(4L, "Dee", "west"));
                em.persist(new JpaBaselineOrderLine(101L, 1L, 7L, "active"));
                em.persist(new JpaBaselineOrderLine(102L, 1L, 8L, "active"));
                em.persist(new JpaBaselineOrderLine(103L, 2L, 7L, "closed"));
                em.persist(new JpaBaselineOrderLine(104L, 3L, 7L, "active"));
                tx.commit();
            }

            var childAuthorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
            var aggregate = new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                    ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), childAuthorization);
            var order = List.of(aggregate, new OrmOrder(ID, false));
            var identity = "postgres-multipage-aggregate-min-customer-v1";
            var source = new JpaOrmQuerySource(postgresFactory);

            var first = source.fetchWithPrivateOrderValues(new OrmFetchRequest(
                    MAPPING, List.of(ID, NAME), List.of(), order, null, 2));
            assertEquals(List.of(1L, 3L), orderedIds(first.rows()));
            assertEquals(2, first.privateOrderValues().size());

            var firstCursor = OrmAggregateCursorPipeline.nextPageCursor(identity, MAPPING, order,
                    first.privateOrderValues().get(1), first.rows().get(1));
            var secondRequest = OrmAggregateCursorPipeline.continuationRequest(firstCursor, identity,
                    MAPPING, List.of(ID, NAME), List.of(), order, 2);
            var second = source.fetchWithPrivateOrderValues(secondRequest);
            assertEquals(List.of(2L, 4L), orderedIds(second.rows()));
            assertEquals(java.util.Arrays.asList(List.of("closed"), java.util.Arrays.asList((Object) null)),
                    second.privateOrderValues());

            var secondCursor = OrmAggregateCursorPipeline.nextPageCursor(identity, MAPPING, order,
                    second.privateOrderValues().get(1), second.rows().get(1));
            var thirdRequest = OrmAggregateCursorPipeline.continuationRequest(secondCursor, identity,
                    MAPPING, List.of(ID, NAME), List.of(), order, 2);
            var third = source.fetchWithPrivateOrderValues(thirdRequest);
            assertTrue(third.rows().isEmpty(), "PostgreSQL terminal cursor must return no rows");

            var concatenated = new java.util.ArrayList<Long>();
            concatenated.addAll(orderedIds(first.rows()));
            concatenated.addAll(orderedIds(second.rows()));
            concatenated.addAll(orderedIds(third.rows()));
            assertEquals(List.of(1L, 3L, 2L, 4L), concatenated,
                    "PostgreSQL pages must preserve ties, NULLS LAST and the primary-key tie-breaker");
            assertTrue(first.rows().stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME)))
                    && second.rows().stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME))),
                    "PostgreSQL private aggregate slots must not leak into semantic projections");
        }
    }

    @Test
    void postgresRejectsAggregateCursorReplayAcrossTenantAndChangedChildAuthorizationWhenConfigured() {
        String url = System.getenv("FOUNDGINE_JPA_POSTGRES_URL");
        assumeTrue(url != null && !url.isBlank(), "PostgreSQL integration service is not configured");
        var properties = new java.util.HashMap<String, Object>();
        properties.put("jakarta.persistence.jdbc.driver", "org.postgresql.Driver");
        properties.put("jakarta.persistence.jdbc.url", url);
        properties.put("jakarta.persistence.jdbc.user", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_USER", "postgres"));
        properties.put("jakarta.persistence.jdbc.password", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_PASSWORD", "postgres"));

        try (var postgresFactory = Persistence.createEntityManagerFactory("foundgine-jpa-postgres", properties)) {
            try (EntityManager em = postgresFactory.createEntityManager()) {
                var tx = em.getTransaction();
                tx.begin();
                em.createQuery("delete from JpaBaselineOrderLine").executeUpdate();
                em.createQuery("delete from JpaBaselineCustomer").executeUpdate();
                em.persist(new JpaBaselineCustomer(1L, "Ann", "north"));
                em.persist(new JpaBaselineCustomer(2L, "Bob", "south"));
                em.persist(new JpaBaselineOrderLine(101L, 1L, 7L, "active"));
                em.persist(new JpaBaselineOrderLine(102L, 1L, 8L, "aaa"));
                em.persist(new JpaBaselineOrderLine(103L, 2L, 7L, "closed"));
                tx.commit();
            }

            var tenantSeven = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
            var tenantEight = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 8L);
            var activeOnly = new OrmPredicate.And(List.of(tenantSeven,
                    new OrmPredicate.Compare(LINE_STATUS, OrmPredicate.Op.EQ, "active")));
            var orderSeven = List.of(new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                    ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), tenantSeven),
                    new OrmOrder(ID, false));
            var orderEight = List.of(new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                    ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), tenantEight),
                    new OrmOrder(ID, false));
            var orderActiveOnly = List.of(new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                    ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), activeOnly),
                    new OrmOrder(ID, false));
            var source = new JpaOrmQuerySource(postgresFactory);
            var originalIdentity = "postgres-customer-v1|effective-child-authorization=tenant:7|all-statuses";
            var tenantEightIdentity = "postgres-customer-v1|effective-child-authorization=tenant:8|all-statuses";
            var activeOnlyIdentity = "postgres-customer-v1|effective-child-authorization=tenant:7|status:active";

            var first = source.fetchWithPrivateOrderValues(new OrmFetchRequest(
                    MAPPING, List.of(ID, NAME), List.of(), orderSeven, null, 1));
            assertEquals(1, first.rows().size(), "PostgreSQL must produce the cursor source row");
            assertEquals(List.of("active"), first.privateOrderValues().get(0),
                    "tenant 8's lexically earlier child must not affect tenant 7's aggregate value");
            var cursor = OrmAggregateCursorPipeline.nextPageCursor(originalIdentity, MAPPING, orderSeven,
                    first.privateOrderValues().get(0), first.rows().get(0));

            assertThrows(IllegalArgumentException.class,
                    () -> OrmAggregateCursorPipeline.continuationRequest(cursor, tenantEightIdentity,
                            MAPPING, List.of(ID, NAME), List.of(), orderEight, 1),
                    "a PostgreSQL cursor from tenant 7 must not decode under tenant 8 authorization");
            assertThrows(IllegalArgumentException.class,
                    () -> OrmAggregateCursorPipeline.continuationRequest(cursor, activeOnlyIdentity,
                            MAPPING, List.of(ID, NAME), List.of(), orderActiveOnly, 1),
                    "changing PostgreSQL child visibility must invalidate the prior cursor");

            var tenantSevenContinuation = OrmAggregateCursorPipeline.continuationRequest(cursor, originalIdentity,
                    MAPPING, List.of(ID, NAME), List.of(), orderSeven, 1);
            var next = source.fetchWithPrivateOrderValues(tenantSevenContinuation);
            assertTrue(next.rows().stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME))),
                    "private aggregate values must remain outside PostgreSQL semantic projections");
        }
    }

    @Test
    void postgresSnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges() {
        String url = System.getenv("FOUNDGINE_JPA_POSTGRES_URL");
        assumeTrue(url != null && !url.isBlank(), "PostgreSQL integration service is not configured");
        var properties = new java.util.HashMap<String, Object>();
        properties.put("jakarta.persistence.jdbc.driver", "org.postgresql.Driver");
        properties.put("jakarta.persistence.jdbc.url", url);
        properties.put("jakarta.persistence.jdbc.user", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_USER", "postgres"));
        properties.put("jakarta.persistence.jdbc.password", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_PASSWORD", "postgres"));
        properties.put("hibernate.connection.isolation", java.sql.Connection.TRANSACTION_REPEATABLE_READ);

        try (var postgresFactory = Persistence.createEntityManagerFactory("foundgine-jpa-postgres", properties)) {
            try (EntityManager em = postgresFactory.createEntityManager()) {
                var tx = em.getTransaction();
                tx.begin();
                em.createQuery("delete from JpaBaselineOrderLine").executeUpdate();
                em.createQuery("delete from JpaBaselineCustomer").executeUpdate();
                em.persist(new JpaBaselineCustomer(1L, "Ann", "north"));
                em.persist(new JpaBaselineCustomer(2L, "Bob", "south"));
                em.persist(new JpaBaselineCustomer(3L, "Cy", "north"));
                em.persist(new JpaBaselineCustomer(4L, "Dee", "west"));
                em.persist(new JpaBaselineOrderLine(101L, 1L, 7L, "active"));
                em.persist(new JpaBaselineOrderLine(102L, 1L, 7L, "closed"));
                em.persist(new JpaBaselineOrderLine(105L, 1L, 8L, "active"));
                em.persist(new JpaBaselineOrderLine(103L, 2L, 7L, "closed"));
                em.persist(new JpaBaselineOrderLine(104L, 3L, 7L, "active"));
                tx.commit();
            }

            var authorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
            var aggregate = new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                    ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), authorization);
            var order = List.of(aggregate, new OrmOrder(ID, false));
            var identity = "postgres-snapshot-aggregate-cursor-v1-tenant-7";

            try (var session = JpaOrmSnapshotPageSession.begin(
                    postgresFactory, JpaOrmSnapshotPageSession.IsolationGuarantee.REPEATABLE_READ_OR_STRONGER)) {
                var source = session.querySource();
                var first = source.fetchWithPrivateOrderValues(new OrmFetchRequest(
                        MAPPING, List.of(ID, NAME), List.of(), order, null, 2));
                assertEquals(List.of(1L, 3L), ids(first.rows()));
                var cursor = OrmAggregateCursorPipeline.nextPageCursor(identity, MAPPING, order,
                        first.privateOrderValues().get(1), first.rows().get(1));

                try (EntityManager writer = postgresFactory.createEntityManager()) {
                    var tx = writer.getTransaction();
                    tx.begin();
                    assertEquals(2, writer.createQuery(
                            "update JpaBaselineOrderLine l set l.status = 'zzz' where l.customerId = 1 and l.tenantId = 7")
                            .executeUpdate());
                    assertEquals(1, writer.createQuery(
                            "update JpaBaselineOrderLine l set l.status = 'aaa' where l.customerId = 2 and l.tenantId = 7")
                            .executeUpdate());
                    tx.commit();
                }

                var continuation = OrmAggregateCursorPipeline.continuationRequest(cursor, identity,
                        MAPPING, List.of(ID, NAME), List.of(), order, 10);
                var second = source.fetchWithPrivateOrderValues(continuation);
                assertEquals(List.of(2L, 4L), ids(second.rows()),
                        "PostgreSQL repeatable-read session must retain its original aggregate snapshot");
                var all = new java.util.ArrayList<Long>();
                all.addAll(ids(first.rows()));
                all.addAll(ids(second.rows()));
                assertEquals(List.of(1L, 3L, 2L, 4L), all,
                        "PostgreSQL snapshot pages must contain every original row exactly once");
                assertTrue(second.rows().stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME))),
                        "private aggregate values must remain outside PostgreSQL semantic projections");
                session.commit();
            }
        }
    }

    @Test
    void postgresCharacterizesAggregateCursorAnomaliesWhenChildAggregatesChangeBetweenPages() {
        String url = System.getenv("FOUNDGINE_JPA_POSTGRES_URL");
        assumeTrue(url != null && !url.isBlank(), "PostgreSQL integration service is not configured");
        var properties = new java.util.HashMap<String, Object>();
        properties.put("jakarta.persistence.jdbc.driver", "org.postgresql.Driver");
        properties.put("jakarta.persistence.jdbc.url", url);
        properties.put("jakarta.persistence.jdbc.user", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_USER", "postgres"));
        properties.put("jakarta.persistence.jdbc.password", System.getenv().getOrDefault("FOUNDGINE_JPA_POSTGRES_PASSWORD", "postgres"));

        try (var postgresFactory = Persistence.createEntityManagerFactory("foundgine-jpa-postgres", properties)) {
            try (EntityManager em = postgresFactory.createEntityManager()) {
                var tx = em.getTransaction();
                tx.begin();
                em.createQuery("delete from JpaBaselineOrderLine").executeUpdate();
                em.createQuery("delete from JpaBaselineCustomer").executeUpdate();
                em.persist(new JpaBaselineCustomer(1L, "Ann", "north"));
                em.persist(new JpaBaselineCustomer(2L, "Bob", "south"));
                em.persist(new JpaBaselineCustomer(3L, "Cy", "north"));
                em.persist(new JpaBaselineCustomer(4L, "Dee", "west"));
                em.persist(new JpaBaselineOrderLine(101L, 1L, 7L, "active"));
                em.persist(new JpaBaselineOrderLine(102L, 1L, 7L, "active"));
                em.persist(new JpaBaselineOrderLine(103L, 2L, 7L, "closed"));
                em.persist(new JpaBaselineOrderLine(104L, 3L, 7L, "active"));
                tx.commit();
            }

            var childAuthorization = new OrmPredicate.Compare(LINE_TENANT_ID, OrmPredicate.Op.EQ, 7L);
            var aggregate = new OrmOrder(LINE_STATUS, false, OrmOrder.AggregateKind.MIN,
                    ORDER_LINE_MAPPING, new OrmLink(ID, LINE_CUSTOMER_ID), childAuthorization);
            var order = List.of(aggregate, new OrmOrder(ID, false));
            var identity = "postgres-live-data-aggregate-cursor-v1-tenant-7";
            var source = new JpaOrmQuerySource(postgresFactory);
            var first = source.fetchWithPrivateOrderValues(new OrmFetchRequest(
                    MAPPING, List.of(ID, NAME), List.of(), order, null, 2));
            assertEquals(List.of(1L, 3L), orderedIds(first.rows()));
            var cursor = OrmAggregateCursorPipeline.nextPageCursor(identity, MAPPING, order,
                    first.privateOrderValues().get(1), first.rows().get(1));

            // Commit concurrent child changes between page requests. Each fetch is a
            // separate read-committed transaction; the cursor does not freeze a snapshot.
            try (EntityManager concurrentWriter = postgresFactory.createEntityManager()) {
                var tx = concurrentWriter.getTransaction();
                tx.begin();
                int movedPastCursor = concurrentWriter.createQuery(
                        "update JpaBaselineOrderLine l set l.status = 'zzz' where l.customerId = 1 and l.tenantId = 7")
                        .executeUpdate();
                int movedBeforeCursor = concurrentWriter.createQuery(
                        "update JpaBaselineOrderLine l set l.status = 'aaa' where l.customerId = 2 and l.tenantId = 7")
                        .executeUpdate();
                assertEquals(2, movedPastCursor);
                assertEquals(1, movedBeforeCursor);
                tx.commit();
            }

            var continuation = OrmAggregateCursorPipeline.continuationRequest(cursor, identity,
                    MAPPING, List.of(ID, NAME), List.of(), order, 10);
            var second = source.fetchWithPrivateOrderValues(continuation);
            var secondIds = orderedIds(second.rows());
            assertEquals(List.of(1L, 4L), secondIds,
                    "live aggregate changes can move a previously returned row after the cursor while moving an unseen row before it");
            assertTrue(first.rows().stream().anyMatch(row -> row.get(ID).equals(1L)) && secondIds.contains(1L),
                    "the changed aggregate for customer 1 demonstrates a duplicate across pages");
            assertFalse(secondIds.contains(2L),
                    "the changed aggregate for customer 2 demonstrates an omission across pages");
            assertEquals(java.util.Arrays.asList("zzz", null), second.privateOrderValues().stream()
                    .map(values -> values.isEmpty() ? null : values.get(0)).toList());
            assertTrue(second.rows().stream().allMatch(row -> row.keySet().equals(java.util.Set.of(ID, NAME))),
                    "private aggregate values must remain outside semantic projections during concurrent changes");
        }
    }

    @Test
    void ownedMutationTransactionCommitsAndRollbackScopeRollsBack() {
        try (EntityManager em = factory.createEntityManager()) {
            var scope = new JpaOrmMutationSource(em).begin();
            em.persist(new JpaBaselineCustomer(40L, "Committed", "west"));
            scope.commit();
        }
        try (EntityManager em = factory.createEntityManager()) {
            assertEquals(1L, em.createQuery("select count(c) from JpaBaselineCustomer c where c.id = 40", Long.class)
                    .getSingleResult());
        }
        try (EntityManager em = factory.createEntityManager()) {
            var scope = new JpaOrmMutationSource(em).begin();
            em.persist(new JpaBaselineCustomer(41L, "Rolled back", "west"));
            scope.rollback();
        }
        try (EntityManager em = factory.createEntityManager()) {
            assertEquals(0L, em.createQuery("select count(c) from JpaBaselineCustomer c where c.id = 41", Long.class)
                    .getSingleResult());
        }
    }

    @Test
    void mutationTransactionJoinsCallerTransactionAndMarksRollbackOnlyOnFailure() {
        try (EntityManager em = factory.createEntityManager()) {
            var tx = em.getTransaction();
            tx.begin();
            var scope = new JpaOrmMutationSource(em).begin();
            scope.rollback();
            assertTrue(tx.isActive());
            assertTrue(tx.getRollbackOnly());
            tx.rollback();
        }
    }
}
