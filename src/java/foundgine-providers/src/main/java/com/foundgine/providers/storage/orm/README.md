# Foundgine ORM provider (Java / Jakarta Persistence)

The Java ORM path is provider-neutral at the semantic/execution layer and uses
Jakarta Persistence (JPA) as its database adapter. The JPA adapter is an
implementation baseline; full EF Core parity is a staged target, not a current
certification claim.

```text
ExecutionIR (validated, authorized)
   -> OrmPlanCompiler
   -> OrmQueries / OrmPredicate
   -> IOrmQuerySource
      -> JpaOrmQuerySource (Criteria tuple query)
      -> InMemoryOrmQuerySource (semantic tests)
   -> ExecutionResult + evidence
```

## Packages

- `storage.orm` is persistence-technology neutral: predicates, registry,
  entity mapping, query/mutation execution providers, cursor primitives, and
  in-memory sources.
- `storage.orm.jpa` contains the optional Jakarta Persistence adapter:
  `JpaOrmQuerySource`, `JpaOrmMutationSource`, `JpaPredicates`,
  `JpaParameterBindings`, and `JpaOrmProvider`. The adapter depends on the JPA API; the consuming
  application supplies its JPA implementation and database driver.

## Current implementation boundary

- Implemented in source: Criteria tuple projection; scalar predicate
  translation with per-execution named and typed `ParameterExpression`
  registration/binding for scalar comparisons and each `IN` member, explicit
  empty/NULL-only `IN` handling, mapped-type checks, and non-orderable
  comparison rejection; scalar ordering, offset/limit,
  resource-local mutation
  transaction participation, managed-entity updates, persist/remove, flush,
  and `EntityManager`/`EntityManagerFactory` wiring.
- Not verified: compilation against the declared Java 27 target, execution
  against a real JPA implementation (JPA-0/JPA-1 CI evidence is pending), SQL
  translation, database runtime,
  dialect portability, concurrency, and Native Image.
- Not implemented in the JPA adapter: relationship quantifiers (`Some`,
  `None`, `All`), aggregate filters (`Count`, `Min`, `Max`), aggregate
  ordering, aggregate cursor pagination, and per-parent child paging.
- Provider-neutral cursor and relationship groundwork must not be interpreted
  as JPA database support. Keep unsupported JPA query shapes fail-closed.

## Generic entity gateway (`JpaEntities`)

`jpa/JpaEntities` gives name-addressed CRUD and semantic reads over every entity bound in an `OrmRegistry`, with no
per-entity code (the counterpart of .NET's `FoundgineEntities`). Persistent classes come from the registry's
`entityTypes`, exactly as for the query source.

- **Reads** run only through a caller-supplied `ReadGate`, which must return an authorized provider plan and its
  execution context. The gateway never builds a read plan, so a read cannot bypass authorization or tenant
  predicates.
- **Writes** (`create`, `update`, `delete`, by single primary key) are mechanical and hold no authorization:
  authorize the capability before calling. They join an active resource-local transaction or own one, like
  `JpaOrmMutationSource`. Attributes are set through fields; entities need a no-arg constructor. Unknown fields and
  key updates are rejected; composite keys are not supported by the by-key operations.
- Status: source and `JpaEntitiesTest` only. Not yet compiled or run against Hibernate/H2 (no Java toolchain was
  available where it was written); it carries the same JPA-0/JPA-1 evidence gate as the rest of the adapter.

## Wiring

```java
var registry = OrmRegistry.create(entities, relationships,
        Map.of("Customer", Customer.class /* ... */),
        Map.of("Customer.Id", "id") /* semantic field -> JPA attribute */);
var compiler = JpaOrmProvider.compiler(registry);
var execution = JpaOrmProvider.execution(registry, entityManagerFactory);
var mutations = new OrmMutationExecutionProvider(JpaOrmProvider.PROVIDER,
        new JpaOrmMutationSource(entityManager), registry);
```

The `EntityManager` overload borrows the caller-managed instance. The
`EntityManagerFactory` overload creates and closes a short-lived manager per
fetch. Mutation execution uses the supplied `EntityManager`; transaction
ownership and JTA support require the integration tests in the parity plan.

## Required next work

See [`src/java/ORM-JPA-PARITY-PLAN.md`](../../../../../../../../../ORM-JPA-PARITY-PLAN.md)
for the full JPA-0 through JPA-9 programme, feature gates, dialect matrix,
security invariants, Native Image distinction, and release criteria. The
machine-readable roadmap is `src/java/conformance/orm-jpa-parity-plan.json`.

Run the Java build from `src/java` with JDK 27 and Maven 3.9+. Until a real JPA
implementation and database-backed suite pass, do not advertise the JPA adapter
as runtime-certified or fully at parity with EF Core.


### JPA scalar type policy (Step 46)

The JPA Criteria adapter binds non-null predicate values through per-execution
typed `ParameterExpression` instances. Range comparisons are restricted to an
explicit portable scalar allowlist (boxed numeric primitives, `BigDecimal`,
`BigInteger`, `String`, and selected `java.time` types). UUID and enum values
may be compared for equality but are not assumed to have portable ordering.
Hibernate/H2 integration fixtures cover decimal precision, large integers,
enum and UUID equality, and Java time values; this coverage remains
implemented-unverified until the Java 27 H2/PostgreSQL CI jobs pass.


### JPA relationship predicates and quantifiers (Steps 47–48)

`OrmPredicate.Exists` expresses a correlated related-entity existence test through
explicit parent/child scalar key bindings and a required child-authorization
predicate. `OrmPredicate.Quantified` adds `SOME`, `NONE`, and `ALL` over the
authorization-visible child set. JPA Criteria translates these as:

- `SOME`: `EXISTS(child WHERE correlation AND authorization AND where)`.
- `NONE`: `NOT EXISTS(child WHERE correlation AND authorization AND where)`.
- `ALL`: `NOT EXISTS(child WHERE correlation AND authorization AND NOT where)`.

`ALL` therefore uses vacuous truth: no authorization-visible children means
`ALL` succeeds. Unauthorized children are outside the quantified set. The
in-memory query and mutation oracles implement the same semantics. Nested
predicate values share the query's typed parameter collector. This does not yet
implement relationship graph materialization or bounded child batching. Runtime
certification remains pending.

### Authorized relationship aggregates (Step 50)

`OrmPredicate.Aggregate` adds correlated `COUNT`, `MIN`, and `MAX` filters over the
authorization-visible related set. Parent/child keys, child authorization, and
the child filter are all applied inside the aggregate subquery. COUNT has a
zero result for an empty set; MIN/MAX ignore null values and yield null when
there are no non-null values. In Foundgine’s two-valued predicate domain, a
comparison involving a null MIN/MAX result is false. Aggregate comparison
constants use the same explicit typed parameter collector as scalar predicates.
The in-memory oracle mirrors these semantics. H2/PostgreSQL runtime certification
remains pending.


### Bounded relationship predicate graphs

Relationship `Exists` and `SOME`/`NONE`/`ALL` predicates may nest, but every query/mutation compilation and in-memory query evaluation first passes through `OrmPredicateValidator`. The validator caps a predicate at 256 nodes, caps relationship nesting at eight, and detects cycles on the active object-identity path. Invalid graphs fail closed before Criteria compilation or oracle evaluation. These limits bound correlated-subquery expansion; they do not replace the still-pending bounded child-batching/materialization implementation.


### Authorized aggregate ordering (Step 51)

`OrmOrder` now represents ordinary field ordering and one-hop authorized `COUNT`,
`MIN`, and `MAX` relationship ordering. Aggregate order terms require a matching
child execution node so the child authorization predicate is explicit; missing
authorization/relationship bindings fail closed. JPA emits correlated aggregate
expressions and private tuple selections. Private selections are not copied into
returned field maps. `MIN`/`MAX` use explicit NULLS LAST ordering in both directions;
`COUNT` is non-null. Aggregate ordering with offset/limit/cursor paging remains
rejected until the private cursor-slot contract is implemented in JPA-5.


### Aggregate cursor groundwork (Step 52)

`OrmAggregateCursor` defines canonical null-rank/value pairs for aggregate order
keys and a reference lexicographic comparator with explicit NULLS LAST semantics.
It validates cursor arity and null-rank consistency. This is not yet an enabled
pagination path: JPA private tuple-value transport, typed correlated aggregate
keyset predicates, query-plan binding, and multi-page dialect integration tests
remain required. Aggregate ordering with pagination therefore continues to fail
closed.


### Typed aggregate continuation descriptors (Step 53)

`OrmAggregateCursor` now decodes each non-null cursor component into its mapped
Java scalar type and emits the canonical lexicographic continuation branches.
Each branch records equality-prefix values, the decisive key, comparison
direction, and whether NULL must be included after a non-null cursor value.
NULLS LAST is independent of ascending/descending value direction; a NULL cursor
component has no later value at that key. These descriptors are not yet compiled
to JPA Criteria predicates, and aggregate pagination remains fail-closed.


### JPA Criteria continuation compiler (Step 54)

`JpaAggregateContinuation` compiles a typed lexicographic continuation over
the same Criteria expressions used for ordering. It binds each non-null cursor
value as a typed parameter, compares according to each key's direction, treats
NULL as later than non-NULL regardless of direction, and preserves equality
prefixes across NULL and non-NULL keys. It supports private correlated aggregate
expressions, but is not wired to public fetch pagination or plan-fingerprint
validation. Aggregate pagination remains fail-closed until those controls and
multi-page database evidence are complete.

### Query-plan-bound aggregate cursor envelope (Step 55)

`OrmAggregateCursorBinding` adds a versioned cursor envelope containing the
private aggregate value slots and a SHA-256 fingerprint of a caller-supplied
canonical, authorization-inclusive plan identity. Decode rejects a different
fingerprint, unsupported version, malformed slot shape, wrong arity, and
invalid typed values. The fingerprint is a cross-plan replay guard, not a
signature: it does not authenticate a client or replace authorization, which
must be rebuilt and applied on every page.

This step deliberately does not enable aggregate paging. The current fetch
contract still returns only semantic field maps, so a provider-neutral private
tuple transport is required before aggregate slots can be carried from JPA
results into page cursors without leaking them. Public fetch integration and
multi-page H2/PostgreSQL tests remain exit gates; aggregate pagination stays
fail-closed.


### Private aggregate tuple transport (Step 56)

`OrmFetchResult` separates semantic result maps from private aggregate ordering
values. JPA tuple queries and the in-memory oracle return one private value list
per row, ordered by aggregate sort-term position; those values are never inserted
into the semantic field map. `IOrmQuerySource.fetchWithPrivateOrderValues` has a
source-compatible semantic-only default for existing custom sources. The execution
pipeline rejects aggregate ordering when a source cannot supply the private channel,
rather than silently dropping cursor material. The private values are retained
inside the internal `OrmQueries.Fetch` result, but public aggregate cursor paging
remains disabled until continuation, plan binding, cursor emission, and multi-page
database tests are fully integrated.

### Private aggregate cursor pipeline (Step 57)

`OrmAggregateCursorPipeline` joins one row's private aggregate tuple values to
its declared, non-null primary-key values and emits the combined tuple through
`OrmAggregateCursorBinding`. Decode validates the caller-supplied canonical
plan fingerprint and restores each slot using its mapped Java type. The helper
then produces the NULLS LAST lexicographic continuation descriptors consumed
by the JPA Criteria continuation compiler; primary-key tie-breakers are always
ascending. NULL aggregate values are preserved, while NULL primary keys are
rejected. Semantic row maps remain unchanged.

This is the typed transport/codec integration layer, not public pagination
activation. `OrmQueries` does not yet derive a stable authorization-inclusive
canonical plan identity from the execution plan or pass decoded aggregate
continuation predicates into `OrmFetchRequest`; aggregate paging therefore
remains fail-closed. The fingerprint remains a replay guard, not a signature.
H2 and PostgreSQL multi-page tests remain required before enabling the feature.


### Aggregate continuation request seam (Step 58)

`OrmFetchRequest.continuationValues` is an optional private tuple aligned with
every `OrmOrder` entry. The JPA source compiles it against the exact same
ordinary or correlated aggregate expressions used for ordering, with typed
parameters and NULLS LAST continuation semantics. Callers must not populate
this field from untrusted cursor text: decode and plan-fingerprint validation
belong at the semantic execution boundary. `OrmQueries` does not yet perform
that integration, so public aggregate cursor paging remains fail-closed.


### Ordered aggregate cursor tuples (Step 59)

`OrmAggregateCursorPipeline.encodeOrdered` now emits cursor slots in the exact
`OrmOrder` sequence, including interleaved ordinary and aggregate terms. Aggregate
values are read only from private tuple transport; ordinary and primary-key values
come from semantic rows. Decode derives mapped Java types from the same order
sequence, and continuation directions are derived from those same terms. This
prevents cursor tuple order from silently diverging from provider `ORDER BY`. It
does not enable public aggregate paging: canonical authorization-inclusive plan
identity derivation and end-to-end multi-page validation remain required.


Aggregate cursor Step 60 provides `continuationRequest` and `nextPageCursor` adapters.
The caller must supply a trusted canonical authorization-inclusive plan identity and
rebuild authorization predicates for each page. `OrmQueries` intentionally continues
to reject public aggregate pagination until it owns that identity and end-to-end
multi-page H2/PostgreSQL tests pass.


### Step 61 — aggregate cursor execution integration

`OrmQueries` now creates the cursor identity internally from the current mapped
execution node, lowered filter, context-resolved authorization, and the exact
provider order sequence (including relationship correlation and child authorization).
It decodes aggregate cursors into the private `OrmFetchRequest.continuationValues`
channel and emits start/end tokens from retained rows plus their private aggregate
slots. Aggregate ordering can use forward limit paging and cursor continuation;
offset paging remains rejected. This is implemented but unverified until the Java
integration suite executes against Hibernate/H2 and PostgreSQL. The SHA-256 fingerprint
prevents cross-plan replay but is not a signature and never replaces authorization.

### Step 62 — multi-page aggregate cursor integration coverage

The Hibernate/H2 integration suite now includes a multi-page aggregate cursor
scenario. It advances through tied `MIN` values, a subsequent non-null aggregate
value, and a final `NULLS LAST` row; it also checks the exhausted terminal page,
exactly-once concatenated ordering, and separation of private aggregate slots from
semantic projections. This test is added but has not been executed in this
workspace. PostgreSQL CI and Java compilation remain required before certification.


### Step 63 — authorization-bound cursor replay regression coverage

The Hibernate/H2 integration suite now constructs a cursor under tenant-7 child
authorization and verifies that it cannot be decoded under tenant-8 authorization.
A second regression changes the authorization-visible child set within the same
tenant (all children versus active-only) and requires cursor invalidation. These
tests exercise the plan-bound cursor envelope and the provider's mapped aggregate
ordering descriptors; they do not replace per-request authorization reconstruction.
They are added but unexecuted in this workspace. PostgreSQL and Java runtime
verification remain release gates.


Aggregate cursor identity now uses length-prefixed atoms and includes cursor schema
version, root/child mapping metadata, selected fields, lowered filters and effective
authorization, relationship correlation, and exact ordering direction. Cursor fingerprints
are replay guards only; they are not cryptographic authenticity tokens.


Step 66 extends the Hibernate/PostgreSQL integration lane with multi-page
aggregate cursor coverage. The CI Surefire evidence gate requires both scalar
PostgreSQL conformance and aggregate cursor pagination to execute without skips;
the tests remain unverified until the hosted Java 27 workflow completes.


Step 67 adds PostgreSQL integration coverage for aggregate cursor replay across
tenant authorization changes and changed child visibility. The test derives its
source cursor from a real PostgreSQL aggregate result and checks that an
unauthorized child row cannot influence the authorized tenant's aggregate value.
The test is required by the CI Surefire evidence gate; it remains
implemented-unverified until the configured Java 27/PostgreSQL workflow executes.


### Aggregate cursor consistency under concurrent writes

Aggregate keyset cursors bind the query/authorization plan, not a database
snapshot. When each page is fetched in a separate read-committed transaction,
concurrent changes to child rows can move a root entity across the cursor
boundary, causing a duplicate or omission. A caller requiring stable export
semantics must keep all page reads in one database snapshot transaction or use
an immutable dataset/version fence or materialized result. The current provider
page-fetch lifecycle does not claim snapshot consistency.


### Opt-in snapshot-consistent page sessions

For exports that must not see child aggregate values move between pages, use
`JpaOrmSnapshotPageSession.begin(factory,
IsolationGuarantee.REPEATABLE_READ_OR_STRONGER)`. Reuse the returned
`querySource()` for every page and call `commit()` only after the complete
export has been consumed. Closing an unfinished session rolls it back.

The isolation value is an explicit deployment assertion: Jakarta Persistence
does not expose a portable transaction-isolation setter or getter. Configure
the datasource/transaction manager for a repeatable database snapshot (or
stronger), and verify that guarantee on each supported database and isolation
configuration. The helper owns a resource-local transaction; it does not support
JTA-managed transactions, does not create a distributed snapshot, and does not
make a per-fetch EntityManagerFactory source snapshot-consistent.


### Snapshot consistency integration coverage

The H2 and PostgreSQL integration suites include a two-transaction scenario: a
page session reads its first aggregate page, a separate writer commits child
aggregate changes, and the page session continues. With repeatable-read
isolation configured, the continuation is expected to reflect the original
snapshot, not the newly committed aggregate values. The PostgreSQL fixture
sets `hibernate.connection.isolation` to JDBC
`TRANSACTION_REPEATABLE_READ`; the H2 test persistence unit uses the same
isolation level. These tests are mandatory CI evidence, not a claim that they
have already executed successfully. A deployment must preserve the configured
isolation through its datasource and transaction manager.
