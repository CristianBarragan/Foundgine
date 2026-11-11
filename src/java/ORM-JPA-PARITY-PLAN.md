# Foundgine Java ORM parity plan — Jakarta Persistence (JPA)

**Target:** make `storage.orm.jpa` the supported Java persistence adapter, with semantic and security behaviour aligned to the .NET EF Core execution contract wherever the underlying persistence API can express it. This is a staged engineering plan, not a claim that the Java adapter is already at parity.

**Architectural rule:** Foundgine remains the semantic execution runtime. JPA is an execution target, not the owner of authorization, semantic planning, capability policy, cursor meaning, or mutation safety.

> **Step 46 — scalar type-family conformance fixtures and portable ordering allowlist implemented; CI evidence pending.** The provider module now includes test-scoped Hibernate ORM 7.4.15.Final with H2 2.5.252, a PostgreSQL persistence unit, SQL statement capture, Criteria tuple-query integration tests, `EntityManager` ownership checks, and owned/caller-owned transaction coverage. The Java JPA workflow provisions PostgreSQL and executes both database lanes. The scalar translator now handles empty and NULL-only IN lists as false, implements Foundgine’s two-valued IN membership contract (ignoring NULL list members and guarding NULL field values), preserving NOT(IN(...)) parity with the in-memory oracle, validates mapped Java value types, rejects ordering on non-orderable attributes, and guards non-null comparisons and IN predicates with IS NOT NULL so Foundgine’s two-valued boolean algebra remains stable under NOT/AND/OR despite SQL UNKNOWN. Tests exercise all six comparison operators, IN edge cases, nested AND/OR/NOT/constants, and explicit IS NULL/IS NOT NULL. These additions are source/test coverage only: JPA-0 and JPA-1 remain unverified until the Java 27 H2/PostgreSQL workflow succeeds. Hibernate’s published 7.4 compatibility matrix currently lists Java through 26, while this repository targets Java 27; Java 27 execution is therefore an explicit compatibility risk to resolve with CI evidence or an upstream-supported provider release.


```text
Semantic Operation
  -> authorization binding
  -> Semantic Plan / ExecutionIR
  -> provider capability and safety gate
  -> provider-neutral ORM plan
  -> JPA Criteria / JPQL compiler
  -> EntityManager / transaction boundary
  -> database dialect
  -> normalized result + execution evidence
```

## 1. Current Java baseline (known, not certified)

The Java tree already contains a provider-neutral ORM package under
`foundgine-providers/.../storage/orm` and a JPA adapter under
`storage/orm/jpa`:

- `JpaOrmQuerySource` creates Criteria tuple queries, selects requested fields,
  composes `OrmPredicate` clauses, orders, and applies offset/limit.
- `JpaPredicates` translates constants, boolean composition, `IN`, null checks,
  and scalar comparisons.
- `JpaOrmMutationSource` loads managed entities for mutation, participates in
  an existing resource-local transaction or owns one, and flushes.
- `JpaOrmProvider` wires the compiler and query source to an `EntityManager` or
  `EntityManagerFactory`.
- The JPA provider ships inside `foundgine-providers` (as the EF Core provider ships inside `Foundgine.Providers`). `jakarta.persistence-api` is a normal transitive dependency; the JPA implementation (Hibernate etc.) is chosen by the application.

This is a useful baseline, not a verified provider integration. The existing
ORM README records that JPA was previously type-checked against API stubs and
was not run against a persistence implementation or database. There is no
basis yet to claim SQL translation, runtime, dialect, concurrency, or native
image parity. The Java parent currently targets Java release 27; the build
runner must use that toolchain before a Maven result can be called valid.

## 2. Parity contract and invariants

Every phase must preserve these invariants before adding feature breadth:

1. **Authorization is mandatory.** Root authorization is a separate,
   non-removable predicate. Child authorization is evaluated inside the child
   query/subquery, not after materialization. Missing required context fails
   closed. Caller filters can only narrow authorized rows.
2. **Projection is explicit.** Select only requested semantic fields plus
   private execution fields (keys, correlation keys, cursor slots) needed by
   the plan. Private fields must never leak into public result cells.
3. **Parameters are bound.** Values are Criteria parameters or typed query
   parameters; never concatenate values into JPQL or SQL.
4. **Capabilities are evidence-gated.** A feature is enabled only after
   provider translation and database runtime tests pass for the declared
   persistence implementation and dialect. Unsupported shapes fail before
   database execution.
5. **Ordering is deterministic.** Every pageable order ends with a unique
   primary-key tiebreaker. Null ordering, collation, numeric precision, and
   timestamp precision are explicit contracts, not dialect assumptions.
6. **Pagination preserves authorization.** Every page query reapplies the
   complete authorization and semantic filter predicate. Cursor contents are
   canonical, typed, versioned, and bound to the plan/order contract.
7. **Mutations are atomic.** Validate the entire batch before writes; preserve
   operation-level authorization filters; enforce one transaction boundary;
   rollback the whole owned batch on failure; never silently commit a caller's
   transaction.
8. **Lifecycle semantics are deliberate.** Flush, clear, optimistic versioning,
   callbacks, cascades, generated identifiers, and persistence-context state
   are covered by tests and documented for callers.
9. **No accidental entity graph traversal.** Query execution must not trigger
   lazy loads or N+1 access. Relationship queries use planned joins/subqueries
   or bounded batched child-key fetches.
10. **Evidence is reproducible.** Record JDK, Jakarta Persistence API,
    persistence implementation/version, database/version, dialect, schema,
    test run, and SQL assertions for each conformance result.

## 3. Workstreams and milestones

### JPA-0 — Freeze the contract and establish a real integration harness

- Turn the current in-memory ORM tests into the provider-neutral semantic
  oracle; add canonical fixtures for tenants, parent/child rows, nulls, ties,
  empty child sets, decimals, timestamps, and mutation conflicts.
- Add test-scoped Jakarta Persistence implementation(s) and a real database.
  Start with Hibernate ORM + H2 for fast CI and PostgreSQL Testcontainers for
  authoritative SQL/runtime checks. Add EclipseLink as a second JPA
  implementation after the first integration suite is stable; do not assume
  Criteria behaviour is identical across implementations.
- Create `persistence.xml` or explicit programmatic bootstrap fixtures, schema
  setup/teardown, deterministic seed data, SQL capture, and test transaction
  utilities. Keep provider dependencies test-scoped; consumers still choose
  their JPA implementation.
- Verify `EntityManager` ownership: caller-managed instances are never closed;
  factory-created instances are closed; transaction scope is explicit.
- Establish baseline tests for scalar projection, predicates, ordering,
  offset/limit, and resource-local mutation against the actual database.

**Exit gate:** Maven compile and tests on the declared Java 27 toolchain; no
hand-written API stubs; SQL and result assertions against H2 and PostgreSQL;
no leaked EntityManager or transaction.

### JPA-1 — Scalar query and predicate parity

> **Step 44 implementation increment:** `JpaPredicates` now rejects value/type mismatches before Criteria path construction; enforces the provider-neutral `isOrderable` policy for range operators; treats empty and NULL-only `IN` sets as false without emitting an illegal empty Criteria `IN`; and removes NULL members from `IN` lists and guards both `IN` and non-null comparisons with `IS NOT NULL`, so NOT/AND/OR preserve Foundgine’s two-valued semantic oracle even for NULL fields. Integration tests cover EQ/NEQ/GT/GTE/LT/LTE, explicit null checks, empty and mixed `IN`, nested AND/OR/NOT, constants, and invalid type/operator combinations. The SQL parameter placeholder assertion remains in place. Step 45 adds a per-execution `JpaParameterBindings` collector. Every non-null scalar comparison value and every non-null `IN` member is represented by a named, field-typed `ParameterExpression`, and both read queries and mutation `find` queries bind those parameters explicitly before execution. Integration cases assert that comparison and `IN` values do not appear in SQL text and that the mutation lookup path also uses placeholders. Step 46 adds real-provider fixtures for exact BigDecimal precision, large BigInteger values, enum and UUID equality, and LocalDate, Instant, and OffsetDateTime comparisons. Range ordering now uses an explicit allowlist of standard boxed numeric types, BigDecimal/BigInteger, String, and supported java.time types; enum and UUID ordering fail closed. This remains source/test coverage only: JPA-0 and JPA-1 are not certified until Java 27 H2/PostgreSQL CI executes successfully.

- Implement typed Criteria parameter binding for equality/inequality,
  comparisons, `IN`, null semantics, empty `IN`, `AND`/`OR`/`NOT`, and constants.
- Validate field existence and Java attribute type before Criteria construction.
  Avoid unchecked raw `Path` comparisons where a typed expression can be used.
- Specify string comparison/collation and case-sensitivity as capability- and
  dialect-specific behaviour; do not assume Java string semantics equal SQL.
- Verify numeric widening, decimal scale, enum mapping, UUID representation,
  temporal precision/time zones, and database null semantics. Step 46 adds
  Hibernate/H2 fixtures for BigDecimal, BigInteger, enum, UUID, LocalDate,
  Instant, and OffsetDateTime; PostgreSQL execution remains a CI exit gate.
- Assert generated SQL uses bind parameters and that tuple aliases/position
  mapping cannot swap or expose private fields.

**Exit gate:** provider-neutral tests and JPA database tests agree for all
supported scalar operations; unsupported type/operator combinations reject
before query execution.

### JPA-2 — Authorization, projection, and relationship traversal

> **Steps 47–49 implementation increments:** Step 47 added provider-neutral `OrmPredicate.Exists(target, link, authorization, where)` using explicit mapped scalar key fields and a mandatory child-authorization predicate. Step 48 adds `OrmPredicate.Quantified` with `SOME`, `NONE`, and `ALL`. JPA Criteria translates these to `EXISTS(auth AND where)`, `NOT EXISTS(auth AND where)`, and `NOT EXISTS(auth AND NOT where)` respectively. `ALL` is vacuously true for an empty authorization-visible child set; unauthorized children are excluded from the quantified domain. The in-memory oracle has matching semantics, and Hibernate/H2 test definitions cover tenant-hidden children and empty sets. All remain implemented-unverified until Java 27 H2/PostgreSQL CI executes successfully. Step 49 adds `OrmPredicateValidator`, invoked before JPA query/mutation compilation and in-memory evaluation. It enforces a 256-node predicate budget, a maximum relationship nesting depth of eight, and identity-based active-path cycle detection; excessive or cyclic graphs fail closed before provider execution. Tests cover the depth and node limits. This bounds nested correlated subquery expansion but does not yet implement relationship graph materialization or bounded child batching. Step 50 adds `OrmPredicate.Aggregate` for authorization-scoped relationship `COUNT`, `MIN`, and `MAX` filters. JPA compiles each into a correlated scalar Criteria subquery; COUNT uses its defined zero value for an empty set, while MIN/MAX ignore null child values and a missing scalar result compares false. The in-memory oracle uses the same authorized child domain and comparison contract. Aggregate values remain parameters, never query text. These aggregate filters are implemented-unverified until Hibernate/H2 and PostgreSQL integration tests execute in CI.


- Bind root authorization to the Criteria query as an independent mandatory
  predicate. Reject an execution context that cannot satisfy a required
  authorization parameter.
- Implement planned relationship traversal using generated relationship
  metadata, explicit join/subquery construction, or the existing bounded
  parent-key child-fetch strategy. Never depend on lazy entity navigation.
- Apply child authorization inside every child query and correlated predicate.
  A parent with no visible children must not reveal that hidden children exist.
- Preserve current result shape: parent rows, child cells, absent-child
  semantics, sibling cross-product semantics, and private join-key handling.
- Batch child fetches by parent key with a bounded parameter count; make the
  chunk size dialect-aware and handle composite keys explicitly.
- Test duplicate relationships, missing children, hidden children, cross-tenant
  references, cycles/depth limits, and SQL query counts to detect N+1 regressions.

**Exit gate:** tenant isolation and child-authorization tests pass against
PostgreSQL; generated SQL proves authorization is in the database predicate;
projection and relationship result shapes match the semantic oracle.

### JPA-3 — Relationship quantifiers and aggregate filters

- Translate relationship `Some`, `None`, and `All` to correlated `EXISTS`,
  `NOT EXISTS`, and the logically equivalent anti-existence form for `All`.
  Define vacuous-truth behaviour for `All` over an empty child set.
- Ensure every correlated subquery includes child authorization and parent/child
  correlation keys. Do not count unauthorized children.
- Add `Count`, `Min`, and `Max` aggregate filters, with explicit empty-set
  semantics (`Count = 0`; `Min`/`Max` produce SQL null and comparisons against
  null results are false in Foundgine's two-valued predicate domain). Step 50
  implements the correlated scalar Criteria subqueries and in-memory oracle.
- Reject unsupported aggregate field types, relationship chains, or nested
  aggregate combinations. Validate that the plan contains exactly the child
  execution/authorization binding required by the aggregate.
- Compare actual SQL and result sets for `Some`/`None`/`All`, Count/Min/Max,
  empty child sets, mixed tenants, and nullable values.

**Exit gate:** each operator has provider-specific SQL and runtime tests on
PostgreSQL and H2; authorization-negative tests prove no hidden-row influence;
capability flags remain off until all required tests pass.

### JPA-4 — Aggregate ordering and private projection slots

- Step 51 adds correlated Count/Min/Max order selectors using the matching child
  execution node's authorization predicate. Missing child-plan bindings fail closed.
- Aggregate expressions are selected into private tuple slots and never copied
  into returned semantic field maps.
- Min/Max use an explicit NULLS LAST rank before the value expression in both
  sort directions; Count is non-null. Aggregate ties append the mapped primary
  key as a deterministic ascending tiebreaker.
- Initial implementation is one relationship hop and aggregate ordering with
  offset/limit/cursor paging remains fail-closed pending JPA-5 private cursor slots.
- Mixed ordinary/aggregate sort terms are supported in the non-paged path; full
  database/dialect ordering certification remains outstanding.

**Exit gate:** SQL assertions and runtime ordering match the same canonical
expected sequence across the supported databases; no pagination is enabled by
this milestone unless JPA-5 is also complete.

### JPA-5 — Canonical keyset pagination and aggregate cursor parity

- Define a versioned cursor contract shared with Foundgine semantics: typed
  values, canonical encoding, strict decode validation, stable sort identity,
  and rejection of malformed, truncated, extra, wrong-type, or non-canonical
  components.
- Support mixed ascending/descending ordinary fields first, then aggregate
  private slots, using lexicographic predicates with explicit null-rank terms.
- Step 52 adds a provider-neutral aggregate cursor codec: every logical value is
  represented by a canonical null-rank/value pair, with strict arity and rank
  validation, plus a reference lexicographic comparator implementing NULLS LAST.
  This is cursor groundwork only; JPA query tuple transport, typed aggregate
  keyset predicate compilation, cursor-plan binding, and multi-page DB proof
  remain outstanding, so aggregate paging stays disabled.
- Step 53 adds typed cursor decoding against the mapped Java value types and
  generates the canonical lexicographic continuation disjunction: equality on
  preceding keys, then a direction-aware comparison on the first differing
  non-null key, with NULL as a later value under NULLS LAST. A NULL cursor key
  has no later value at that position. These are provider-neutral predicate
  descriptors, not compiled JPA predicates; aggregate paging remains disabled.
- Step 54 adds `JpaAggregateContinuation`, a Criteria compiler for strict
  lexicographic continuation over the exact ordering expressions, including
  correlated aggregate subqueries. It creates typed parameters, equality-prefix
  predicates, direction-aware non-null comparisons, and direction-independent
  NULLS LAST branches. The compiler is covered by a Hibernate/H2 integration
  test for a mixed-key continuation. It is not yet connected to the public fetch
  request or bound to a semantic query-plan fingerprint; aggregate paging remains
  fail-closed pending those gates and exhaustive multi-page dialect tests.
- Step 55 adds `OrmAggregateCursorBinding`, a versioned envelope for private
  aggregate cursor slots bound to a SHA-256 fingerprint of a caller-supplied
  canonical, authorization-inclusive plan identity. Decode validates version,
  fingerprint, slot shape, arity, and mapped Java types. The fingerprint is a
  cross-plan replay guard, not a signature or authorization mechanism; every page
  must rebuild authorization. The current fetch result contract still lacks
  provider-neutral private tuple transport, so public fetch integration and
  multi-page H2/PostgreSQL proof remain outstanding and aggregate paging stays
  fail-closed.
- Step 56 introduces `OrmFetchResult` with two strictly separate channels: semantic
  projected rows and per-row private aggregate ordering values. `IOrmQuerySource`
  retains source compatibility through a default semantic-only method; JPA tuple
  queries and the in-memory oracle override it and return aggregate values without
  adding them to result maps. `OrmQueries` preserves the private channel internally
  across fetched chunks. A regression test verifies that aggregate values remain
  aligned with rows and that unselected key fields do not leak into semantic maps.
  This establishes transport only; aggregate cursor paging remains fail-closed
  until typed continuation, plan binding, page-token emission, and multi-page
  database evidence are connected and verified.
- Step 57 adds `OrmAggregateCursorPipeline` to combine private aggregate tuple
  values with non-null primary-key tie-breakers and the existing versioned,
  plan-fingerprinted cursor envelope. It validates mapped value types on decode
  and produces continuation branch descriptors. The public query pipeline still
  does not derive the canonical plan identity or supply decoded values to fetch.
- Step 58 extends `OrmFetchRequest` with an optional private continuation tuple,
  aligned one-to-one with the complete order list. The JPA Criteria source now
  builds continuation over the exact ordinary and correlated aggregate
  expressions used by `ORDER BY`, using typed parameter bindings and explicit
  NULLS LAST semantics. A Hibernate/H2 integration test exercises a MIN
  aggregate cursor boundary across tied values and an empty relationship. This
  is a provider-level integration seam only: `OrmQueries` does not yet decode
  the public cursor, derive the authorization-inclusive canonical identity, or
  populate the continuation tuple. Aggregate paging therefore remains
  fail-closed at the semantic fetch boundary pending that integration and
  multi-page dialect evidence.
- Step 59 adds ordered cursor tuple handling for interleaved aggregate and
  ordinary sort keys. `encodeOrdered` obtains aggregate values exclusively from
  the private tuple channel and ordinary/tie-breaker values from the semantic row;
  `decodeOrdered` derives mapped Java types from the exact `OrmOrder` sequence;
  `continuationOrdered` derives direction flags from that same sequence. This
  closes an ordering-shape gap in the lower-level cursor pipeline, but does not
  activate semantic aggregate paging or establish a canonical authorization-
  inclusive plan identity in `OrmQueries`.
- Step 60 adds `continuationRequest` and `nextPageCursor` to the provider-neutral
  aggregate cursor pipeline. The request helper decodes the versioned,
  plan-fingerprinted cursor against the exact `OrmOrder` sequence and carries the
  resulting typed tuple through `OrmFetchRequest.continuationValues`; the emission
  helper uses private aggregate slots plus semantic ordinary/tie-breaker fields.
  Tests cover request construction and cross-plan replay rejection. This is the
  final provider request/codec adapter seam, not yet an activation of semantic
  aggregate paging: `OrmQueries` still rejects aggregate limit/cursor requests,
  and the trusted execution planner has not yet supplied a canonical
  authorization-inclusive identity. Multi-page H2/PostgreSQL proof remains
  mandatory.
- Include the unique primary-key tiebreaker in the encoded order tuple. Ensure
  cursor values are lossless for `Long`, decimal, temporal, and supported string
  types; do not silently round or coerce.
- Reapply root and child authorization on every page. Bind cursor validity to
  entity, filter/authorization-plan fingerprint, sort terms, and cursor schema
  version so a cursor cannot be replayed against a different plan.
- Preserve `hasNextPage`, previous-page, and count semantics. Test page size 1,
  empty pages, tied aggregate values, inserts/deletes between pages, nulls,
  collation, and no duplicate/omitted rows across page boundaries.
- Keep aggregate cursor pagination fail-closed until all supported dialects
  pass the full multi-page integration suite. If concurrent-write snapshot
  semantics cannot be guaranteed, document that separately from keyset
  stability.

**Exit gate:** exhaustive multi-page tests on PostgreSQL and H2 prove stable
boundaries and authorization preservation; malformed cursors fail closed;
feature is enabled only for tested type/dialect combinations.

### JPA-6 — Mutation and transaction parity

- Validate all operations, field assignments, filters, conflict keys, and
  dependencies before the first write.
- Preserve create/update/delete/upsert semantics, returned fields, generated-key
  propagation to later operations, and no-filter update/delete rejection.
- Specify transaction ownership for resource-local and JTA transactions.
  Joining a caller transaction must never commit it; failures mark it
  rollback-only where required. Owned transactions rollback on any failure.
- Test optimistic locking/version conflicts, flush ordering, generated IDs,
  callbacks, cascades, constraint failures, partial batch failures, and rollback
  visibility from a fresh EntityManager.
- Ensure delete return projections are captured before removal and that updates
  do not accidentally overwrite fields not present in the mutation plan.
- Add explicit concurrency strategy for read-modify-write mutations; do not
  claim atomic compare-and-set semantics without locking/version evidence.

**Exit gate:** integration tests prove atomic rollback and caller-transaction
ownership, with SQL and affected-row evidence. Security conformance is evaluated
per mutation shape, not inferred from query-provider support.

### JPA-7 — Dialect and implementation conformance matrix

- Maintain a matrix keyed by Jakarta Persistence implementation/version and
  database/dialect/version, not just by the `JPA` label.
- Required first tier: Hibernate ORM + H2 and Hibernate ORM + PostgreSQL.
  Second tier: EclipseLink + H2/PostgreSQL. Add MySQL/MariaDB and SQL Server
  only when there is a supported user requirement and CI capacity.
- Capture normalized SQL, bound parameter types, execution plans for critical
  queries, and query counts. Test `IN` parameter limits, composite keys,
  pagination syntax, correlated subqueries, null ordering, and identifier
  quoting per dialect.
- Define a capability profile for each tested combination. The intersection
  is the portable default; implementation-specific extensions remain explicit
  opt-ins and are not advertised as universal JPA behaviour.
- Run the same semantic fixtures through in-memory, JPA, and the .NET EF Core
  adapter and compare normalized result/evidence contracts.

**Exit gate:** checked-in machine-readable matrix; every `certified` entry links
an executed CI artifact with exact versions and dialect. Untested combinations
remain unsupported or unverified.

### JPA-8 — Java AOT / Native Image readiness

- Treat Java Native Image as a separate target from .NET Native AOT. JVM
  compilation and JPA runtime success do not establish Native Image support.
- Inventory reflection, dynamic proxies, bytecode enhancement, entity
  constructors, annotation scanning, service loading, resource bundles, and
  persistence-provider bootstrap requirements.
- Prefer generated static metamodels and build-time registration over runtime
  classpath scanning where feasible. Provide native-image reachability metadata
  only for the supported persistence implementation and exact version.
- Build and run a minimal native executable using the same authorization,
  projection, relationship, aggregate, and mutation smoke paths. Measure startup
  and memory only after semantic correctness is established.
- Do not advertise universal JPA Native Image support: certify exact
  implementation/database combinations and retain JVM as the baseline.

**Exit gate:** native-image build and native execution on supported CI targets,
with no unexplained reflection/proxy failures and full semantic smoke markers.

### JPA-9 — Operational hardening and release certification

- Add structured diagnostics: provider, implementation/version, dialect,
  capability profile, plan fingerprint, query count, elapsed time, rows,
  pagination mode, transaction outcome, and rejection reason. Never log bound
  secrets or sensitive values.
- Add cancellation/timeouts and statement/lock timeout propagation where JPA
  and the provider expose them. Define what cancellation means after a write
  transaction has started.
- Add resource-leak, connection-pool, persistence-context growth, and large
  result-set tests. Ensure streaming/scrolling is not enabled without clear
  EntityManager lifetime and back-pressure semantics.
- Publish a Java parity report that distinguishes implementation, translation,
  runtime, dialect, and native-image evidence. Pin dependency versions and
  produce reproducible Maven artifacts/SBOMs.

**Exit gate:** all prior required gates pass, supported-version matrix is
reviewed, CI evidence is retained, and release notes state exact limitations.

## 4. Feature parity checklist

| Semantic capability | Existing Java baseline | Planned JPA milestone | Enablement rule |
|---|---|---|---|
| Scalar filter / boolean composition | Partial Criteria translation | JPA-1 | Real DB tests for each operator/type |
| Selected-field tuple projection | Implemented in source | JPA-1 | SQL projection + no entity/lazy-load test |
| Root authorization | Predicate pipeline exists; DB integration unverified | JPA-2 | Cross-tenant negative SQL/runtime tests |
| Relationship traversal | Existing ORM child-fetch architecture | JPA-2 | Child authorization and query-count tests |
| Some / None / All | Unsupported | JPA-3 | Correlated SQL + empty-set semantics |
| Count / Min / Max filters | Implemented-unverified in Step 50 | JPA-3 | H2/PostgreSQL authorized correlated aggregate tests |
| Count / Min / Max ordering | Implemented-unverified for non-paged one-hop orders in Step 51 | JPA-4 | SQL assertions, auth-negative tests, dialect null policy, stable ties |
| Ordinary keyset cursors | Existing provider-neutral cursor groundwork; JPA runtime unverified | JPA-5 | Canonical codec and multi-page DB tests |
| Aggregate keyset cursors | Typed decode and continuation-branch foundation in Step 53; execution unsupported | JPA-5 | Private tuple transport, typed lexicographic SQL predicates, plan binding, mixed tuple/auth/tie tests |
| Atomic mutation batches | Basic resource-local adapter exists | JPA-6 | Real rollback, version, generated-key tests |
| Dialect portability | Not established | JPA-7 | Versioned implementation/dialect matrix |
| Native Image | Not established | JPA-8 | Exact implementation native build + execution |
| Diagnostics / cancellation | Partial common runtime support; JPA propagation unverified | JPA-9 | Runtime evidence and redaction tests |

## 5. Required test layers

1. **Pure semantic tests:** compiler validation, authorization binding, cursor
   canonicalization, capability gating, and normalized result contracts. Fast,
   deterministic, no JPA implementation required.
2. **JPA implementation tests:** Criteria tree construction and EntityManager
   lifecycle using the chosen provider implementation.
3. **Database integration tests:** actual SQL execution, SQL capture, dialect
   differences, transactions, concurrency, and query counts.
4. **Cross-runtime parity tests:** same fixtures and expected outputs against
   Java JPA and .NET EF Core; compare semantic results, not provider-specific SQL
   strings except in dialect-specific tests.
5. **Native Image tests:** separate from JVM tests, with exact runtime metadata
   and semantic success markers.
6. **Security regression tests:** unauthorized root, unauthorized child,
   missing context, forged cursor, cursor replay across plan, unfiltered
   mutation, rollback after partial failure, and side-channel checks for hidden
   relationship existence.

## 6. CI, evidence, and release policy

- Add a Java ORM workflow that runs the Java 27 toolchain, provider-neutral
  tests, JPA/H2 tests, and PostgreSQL integration tests. Use service containers
  or Testcontainers with deterministic database versions.
- Publish a machine-readable Java JPA matrix with separate statuses for
  implementation, translation, runtime, dialect, and Native Image execution.
- Upload test reports, SQL evidence, dependency/version manifest, and native
  image logs as immutable CI artifacts.
- Make the portable capability profile the intersection of passing tested
  implementation/dialect combinations. A test definition or successful compile
  is not a runtime pass.
- Release gate requires all mandatory semantic/security tests, no regression
  in supported EF Core behaviour, no unsupported feature accidentally enabled,
  and complete evidence for each capability advertised in the release.

## 7. Execution order

The critical path is **JPA-0 → JPA-1 → JPA-2 → JPA-3 → JPA-4 → JPA-5 → JPA-6**.
JPA-7 runs continuously alongside feature work; JPA-8 begins after the JVM
adapter is stable; JPA-9 closes the release. Do not implement aggregate cursor
pagination ahead of authorization-aware aggregate ordering and ordinary cursor
conformance. Do not enable a capability because its Criteria API expression
can be constructed: require SQL translation and runtime evidence first.

#### Step 61 — execution-plan-bound aggregate cursor integration

- `OrmQueries` now derives the aggregate cursor identity internally from the mapped
  root entity, selected semantic fields, lowered filter, context-resolved root
  authorization, exact provider ordering sequence, relationship correlation, and
  resolved child authorization predicates. Callers cannot supply or override this
  identity.
- Aggregate cursor requests are decoded against that identity and mapped ordering
  types, then passed to `OrmFetchRequest.continuationValues`; the JPA source compiles
  the continuation against the same expressions used for `ORDER BY`.
- Start/end cursors are emitted from each retained row and its separately transported
  private aggregate tuple. Ordinary ordering and primary-key values remain in the
  semantic row; aggregate values never enter the semantic projection.
- Aggregate ordering now participates in limit-based forward paging and cursor
  continuation. Offset paging with aggregate ordering remains rejected.
- This is source-level integration only. Multi-page Hibernate/H2 and PostgreSQL
  execution, cross-tenant replay tests, and Java compilation remain mandatory before
  advertising aggregate cursor pagination as certified. The fingerprint is a replay
  guard, not a signature or an authorization substitute.


#### Step 62 — multi-page aggregate cursor integration coverage

- Added a Hibernate/H2 integration scenario that fetches aggregate-ordered rows in
  multiple bounded pages, emits each next-page cursor from the private aggregate
  tuple, and feeds the decoded tuple back through the provider continuation request.
- The scenario crosses an aggregate tie (`MIN = active`), then a later aggregate
  value (`closed`), and finally the `NULLS LAST` row. It verifies that concatenated
  pages equal the complete deterministic result exactly once, that the terminal
  cursor returns no rows, and that private aggregate slots never appear in the
  semantic projection.
- This is integration-test source coverage, not an executed database result in this
  environment. H2/PostgreSQL CI execution and Java compilation remain release gates;
  aggregate cursor pagination stays `implemented-unverified`.


#### Step 63 — authorization-bound aggregate cursor replay tests

Added Hibernate/H2 integration regressions proving that an aggregate cursor issued
under tenant-7 child authorization is rejected under tenant-8 authorization, and
that changing the authorization-visible child set within a tenant invalidates the
prior cursor. This protects cursor reuse across effective-authorization changes;
it does not make the cursor a signature or replace rebuilding authorization on each
page. Tests remain implemented-unverified until the Java/Hibernate and PostgreSQL
suites execute in CI.


#### Step 64 — canonical plan identity dimension regressions

- Strengthened the internally generated aggregate cursor identity with length-prefixed
  canonical atoms, explicit cursor schema version, root mapping metadata (field IDs,
  persistent names, Java types, primary-key flags and columns), selected fields,
  lowered root filter and authorization, child mapping/selection/filter/authorization,
  relationship correlation and the exact ordering direction/aggregate sequence.
- Added codec regressions proving that changes to tenant authorization, filters, sort
  direction, relationship mapping and cursor schema invalidate an existing cursor,
  and that embedded delimiters cannot create ambiguous atom boundaries.
- The fingerprint remains a deterministic replay guard, not a MAC/signature. The
  canonical identity still depends on deterministic rendering of the lowered predicate
  records; cross-process stability and Java/Hibernate execution must be verified in CI.
  This increment remains implemented-unverified.


#### Step 65 — Java CI toolchain and execution-evidence gate

- Added a Java 27 preflight to the JPA workflow. CI prints the actual Java and Maven
  versions and fails if the compiler/runtime toolchain is not Java 27.
- Added `verify-jpa-ci-evidence.py`, which inspects Surefire XML and requires the
  aggregate ordering, multi-page no-duplicate/no-omission, cross-tenant replay,
  changed-child-authorization, typed continuation, and PostgreSQL integration test
  methods to have executed. A skipped PostgreSQL lane now fails the evidence gate.
- This closes a reporting gap: a green Maven run alone is not accepted if critical
  provider/database methods were skipped. It does not establish Java 27 support in
  Hibernate 7.4, nor does it substitute for a successful hosted workflow run.
- Step 65 remains implemented-unverified until the Java 27 workflow succeeds and
  the H2/PostgreSQL reports are inspected.


#### Step 66 — PostgreSQL aggregate cursor pagination evidence

- Added a PostgreSQL-backed Hibernate integration scenario that seeds the same
  aggregate-order fixture used by H2 and fetches three consecutive pages. It
  checks deterministic ordering across tied `MIN` values, the later aggregate
  value, `NULLS LAST`, and the primary-key tie-breaker, including terminal-page
  exhaustion and exact concatenated row order.
- The scenario also verifies private aggregate tuple slots remain separate from
  semantic projections on PostgreSQL.
- Extended the Surefire evidence gate so the PostgreSQL aggregate pagination
  method must execute (not be skipped) alongside the scalar PostgreSQL method.
- This is additional integration-test source and CI enforcement, not proof of a
  passing hosted run. Step 66 remains implemented-unverified until Java 27 CI
  executes the complete H2/PostgreSQL suite and the reports are reviewed.


#### Step 67 — PostgreSQL authorization-bound aggregate cursor replay

- Added a PostgreSQL-backed Hibernate integration method that seeds a child row
  in another tenant with a lexically earlier aggregate value, then verifies the
  authorized tenant's private aggregate tuple excludes that row.
- The test mints a plan-bound cursor from the real PostgreSQL result and verifies
  that decoding fails when replayed with a different tenant authorization or
  with narrower child visibility in the same tenant. It also continues with the
  original authorization and verifies private aggregate slots remain outside
  semantic projections.
- Extended the Surefire evidence gate and parity-plan validator to require this
  PostgreSQL security regression method to execute rather than be skipped.
- This is integration-test source and CI enforcement, not a passing database
  result. Step 67 remains implemented-unverified until Java 27 CI executes the
  H2 and PostgreSQL suites successfully and the Surefire reports are reviewed.


#### Step 68 — PostgreSQL concurrent aggregate data-change characterization

- Added a PostgreSQL-backed integration test that fetches the first aggregate
  cursor page, commits child-row aggregate changes through a separate writer
  transaction, then continues from the original cursor.
- The test deliberately characterizes the live-data semantics under separate
  read-committed page requests: changing a previously returned customer's MIN
  value can move it after the cursor and make it reappear; changing an unseen
  customer's MIN value can move it before the cursor and make it disappear
  from later pages. Private aggregate slots remain outside semantic projections.
- This is an important limitation, not a correctness guarantee: keyset cursors
  and plan-bound fingerprints do not provide a database snapshot. Callers that
  require a duplicate-free, omission-free export across concurrent writes must
  execute all pages in one database snapshot transaction, or use an explicit
  immutable dataset/version fence or materialized result. The current per-fetch
  EntityManager lifecycle does not establish such a shared snapshot.
- CI's Surefire evidence gate now requires this PostgreSQL characterization
  method to execute and rejects a skipped PostgreSQL lane. The test remains
  implemented-unverified until Java 27 CI runs it against PostgreSQL.


#### Step 69 — explicit JPA snapshot page-session contract

- Added `JpaOrmSnapshotPageSession`, an opt-in resource-local session that owns
  one `EntityManager` and one transaction for an entire multi-page read. The
  caller reuses its single `IOrmQuerySource` for every page; close without an
  explicit commit rolls back rather than committing a partial export.
- The API requires an explicit `REPEATABLE_READ_OR_STRONGER` isolation
  acknowledgement. This is deliberately an assertion, not a false promise: JPA
  has no portable API to set or inspect database isolation. Deployment
  configuration and dialect-specific integration evidence must establish the
  actual snapshot guarantee.
- Added an H2 integration test for session lifecycle and fail-after-close
  behavior. This does not certify snapshot isolation or PostgreSQL behaviour.
- The existing EntityManagerFactory-per-fetch path remains live-data/read-committed
  semantics and does not silently opt into snapshot guarantees. Aggregate cursor
  pagination remains implemented-unverified pending Java 27/H2/PostgreSQL CI.


#### Step 70 — H2 and PostgreSQL snapshot consistency under concurrent writes

- Added a real H2 integration test that opens `JpaOrmSnapshotPageSession`,
  fetches the first aggregate cursor page, commits child-row aggregate changes
  through a separate EntityManager, and fetches the continuation through the
  original session. The expected second page is the original snapshot ordering,
  with every root row returned exactly once.
- Added the equivalent PostgreSQL integration test. Its dedicated persistence
  factory explicitly configures Hibernate transaction isolation to JDBC
  `TRANSACTION_REPEATABLE_READ` before the session starts.
- Configured the H2 test persistence unit for repeatable-read isolation and
  extended the Surefire evidence gate to require both snapshot tests to execute;
  the PostgreSQL snapshot test must not be skipped when the PostgreSQL CI lane
  is configured.
- These are test definitions and explicit database configuration, not runtime
  evidence. H2 and PostgreSQL snapshot guarantees remain implemented-unverified
  until the Java 27 CI run executes both tests successfully. Deployments must
  independently ensure the connection pool and transaction manager do not
  downgrade isolation.

#### Step 71 — typed JPA repository foundation

- Added `JpaOrmRepository<T>`, the typed counterpart to the EF Core generic CRUD
  repository. It exposes the caller-owned `EntityManager`, Criteria-backed query,
  get/list/exists/count, insert, merge-update, attribute update, and delete.
- Writes join a caller-owned resource-local transaction without committing it, or
  create/commit their own transaction. Repository-owned materialized entities are
  detached after CRUD operations. `list` detaches its result rows because portable
  JPA has no equivalent to EF Core's `AsNoTracking`.
- Added an AOT-oriented constructor that consumes `JpaEntityModel<T>` and uses its
  generated direct writers for named-attribute updates. The class-based constructor
  remains a reflective compatibility path for attribute writes; it is not the
  Native Image-safe path.
- Added Hibernate/H2 integration test definitions for CRUD, generated-accessor
  updates, primary-key update rejection, and caller-owned transaction rollback.
- The existing Java `TypedQuery<T>` / `DynamicQuery` / `FoundgineQuery` already
  provide provider-neutral semantic query authoring over `IFoundgine`; the JPA
  provider is selected through the engine's provider registration. This is not yet
  a parity port of the C# `FoundgineEfReader` convenience facade: the Java runtime
  currently exposes its `ReadIntent` API rather than the newer C# `SemanticOperation`
  reader surface. That adapter remains open.
- Existing `FoundgineAotProcessor`, `JpaEntityModel`, `JpaEntities`, and
  `OrmRegistry.create(...)` provide generated direct-access models and metadata
  registration. A unified fluent JPA model-configuration facade and Hibernate
  Native Image reachability configuration/native executable integration tests are
  still open.

**Evidence:** source and test definitions added; not compiled or executed in this
container. The available runtime is OpenJDK 21, while the project targets Java 27;
Maven and cached Jakarta/Hibernate dependencies are also unavailable. Step 71 is
therefore **implemented-unverified**, not complete. Do not infer Hibernate 7.4
Java 27 compatibility, Native Image compatibility, or database test success from
these source additions.

#### Step 72 — fluent `FoundgineOrm`, generated entity maps, semantic reader (checkpoint 1)

- `JpaEntityModel<T>` gained `attributes()` and `idAttribute()` (full persistent attribute set and the single
  `@Id` attribute) via a new constructor; the four-argument constructor is unchanged.
- `@FoundgineOrmEntity` gained `entity()`. `FoundgineAotProcessor` (both copies, kept identical) now emits
  `GeneratedOrmMaps` (semantic entity name -> persistence class) and passes attributes/id into
  `GeneratedJpaModels`. Name resolution: explicit `entity()`, then `@FoundgineEntity`, then class name minus `Entity`.
- Added `FoundgineOrm<C>`: immutable fluent builder over metadata + entity map + generated models + a mandatory
  `Authorization<C>` binding. Build fails closed without authorization, on unknown semantic entities, on a mapped
  entity without a generated model (unless `allowReflection()`), and on attributes the entity does not declare.
- Added `FoundgineJpaReader<C>`, the Java counterpart of the C# EF reader facade: every read goes through
  authorization -> plan -> ExecutionIR -> provider plan -> JPA Criteria. Also `FoundgineOrm.repository(...)`
  and `FoundgineOrm.entities(caller, em)`.
- **Evidence:** the annotation processor was compiled and run on JDK 21 over the supply-chain sample entities and
  produced the expected `GeneratedOrmMaps` and `GeneratedJpaModels` (attribute sets and `"id"` identifiers). The
  generated metadata file and everything depending on foundgine-core could not be compiled in this session (no
  Jackson or Jakarta jars, no network). `FoundgineOrm` and `FoundgineJpaReader` are therefore
  **implemented-unverified**.
#### Step 72 — checkpoint 2

- Sample: `SupplyChainOrm` (generated metadata, maps and models through `FoundgineOrm`), a metadata-addressed
  `SupplyChainPolicy.create(MetadataRegistry, caller)` overload, and `SupplyChainOrmTest` (fail-closed builds,
  authorization on the plan path, tenant isolation and field dropping against the in-memory source). The existing
  `SupplyChainSemantics` path is unchanged. Also corrected `SupplyChainJpa` to import the generated classes from
  the sample's generated package (`com.foundgine.samples.supplychain.generated`), which the pom's processor option
  sets; the previous `com.foundgine.generated` import would not have resolved.
- New reactor module `foundgine-spring` (`FoundgineJpaAutoConfiguration`, `FoundgineOrmCustomizer`). It registers
  `FoundgineOrm` only when an `Authorization` bean exists. Excluded from Central publishing until you decide it is
  a fifth public library. The Spring Boot version pin is a guess; the context-runner test is still open.
- New module `samples/foundgine-jpa-native`: `NativeSmoke` (H2, generated-model repository insert, authorized
  read), a `native` Maven profile and a seed `reachability-metadata.json`. The metadata is a seed only and must be
  completed with the tracing agent on a GraalVM build.
- **Evidence:** none of checkpoint 2 was compiled or run. This session had no Foundgine core build (Jackson jars
  absent), no Jakarta/Hibernate/H2/Spring jars and no GraalVM. All of Step 72 stays **implemented-unverified**.

**Java 27 CI checklist:** build the reactor; run `SupplyChainOrmTest`, `SupplyChainSemanticsTest` and the processor
parity test; run `NativeSmoke` on the JVM; then `-Pnative`, complete the reachability metadata with the agent, and
rerun. Confirm the Spring Boot pin and add a context-runner test.

#### Step 72 — checkpoint 4 (samples)

- Starter: `SupplyChainJpa` now delegates to `SupplyChainOrm`/`FoundgineOrm` (reader, repository, entity gateway);
  `Main` also shows the typed repository. `SupplyChainSemantics` is kept as the baseline for its existing test.
- Advanced: `WarehouseEntity`/`InventoryLotEntity` are `@FoundgineOrmEntity` JavaBeans (public no-arg constructor and
  setters, so generated models can construct and write them). The module's pom runs the annotation processor into
  `...advanced.generated`. New `AdvancedJpaOrm` configures `FoundgineOrm` from the fluent model; `AdvancedJpaPolicy`
  gained a metadata-addressed `create`; `AdvancedJpaSemantics` and `AdvancedJpaReads` are now facades. New
  `AdvancedJpaOrmTest`.
- **Evidence:** the annotation processor was run on JDK 21 over the advanced entities and produced `GeneratedOrmMaps`
  (`InventoryLot`, `Warehouse`) and models with the expected attribute sets and `"id"` identifiers. Everything else
  is uncompiled and unrun (no core build, Jackson, Jakarta, Hibernate, H2, Spring or GraalVM here). Still
  **implemented-unverified**.
- **Not done:** the tutorial markdown files (`SupplyChain-Starter-Tutorial.md`, the Explained doc, the advanced
  tutorial) still show the pre-`FoundgineOrm` code listings; and I had no C# source, so "same as C#" is matched only to
  the surface named in the plan, not compared line by line.

#### Step 72 — checkpoint 5 (compared against `csharp-updated.zip`)

Compared with `Foundgine.Providers.EntityFrameworkCore` (`FoundgineEfReader`, `FoundgineEfRepository`,
`FoundgineEntities`, `FoundgineEfModel`, `IdentityEntityMap`, `AddFoundgineEf`):

| C# | Java |
| --- | --- |
| `FoundgineEfReader.ReadAsync(SemanticOperation)` + `FoundgineReadResult(Rows, Fingerprint)` | `FoundgineJpaReader.readResult(caller, op)` -> `ReadResult(rows, fingerprint)`; fingerprint is `SemanticPlanFingerprint.create(plan)`, **not** the C# SHA-256 of the semantic+execution plan JSON, so values will not match C# |
| `ReadAsync(entity, fields, filter, order)` / `ReadAsync(SemanticQuery)` / `From(entity)` / lambda form | `read(caller, entityId, fields, equals)` (equality filters only, no ordering overload; use the lambda form) / `read(caller, SemanticQuery)` / `from(entity)` / `read(caller, entity, q -> ...)` |
| `IFoundgineRepository<T>`: Get/List/Exists/Count/Add/Update/Update(property)/Delete, `Query()` no-tracking | `JpaOrmRepository<T>`; added `update(id, attribute, value)` (string attribute instead of an expression selector) |
| `FoundgineEntities` | `JpaEntities` (existing), obtained through `orm.entities(caller, em)` |
| `FoundgineEfOptions.AddMaps` / `MapClrTypes` | `Builder.entities(...)` (name -> class), `Builder.attribute(...)` overrides |
| `FoundgineEfOptions.Capabilities` | not ported (the Java ORM plan compiler takes no capability profile here) |
| `ConfigureEntity` / `Customize(ModelBuilder)` / `FoundgineDbContext` derived from metadata | not applicable: JPA entities are class-annotated; no metadata-derived JPA model |
| `IdentityEntityMap<T>` (domain class is the persistence class) | supported by putting `@FoundgineEntity` and `@FoundgineOrmEntity` on one class (the processor test fixture does this); no runtime identity-map helper |
| `AddFoundgineEf(...)` one-line DI with per-entity `IFoundgineRepository<T>`, reader, `UseEngineProvider()` | `foundgine-spring` registers only `FoundgineOrm`; **no** per-entity repository beans, reader bean or engine-provider registration yet |
| Model/ORM mismatch fails at startup | `FoundgineOrm.build()` fails closed on the same class of mismatch |

**Evidence:** none of this compiled or ran. Still **implemented-unverified**.

### Step 72, checkpoint 5 (implemented-unverified; nothing here was compiled, no Maven in the authoring session)

- `JpaOrmRepository.update(id, attribute, value)` now delegates through `Collections.singletonMap`, which accepts a
  null value (sets the column to null). New `JpaOrmRepositoryIntegrationTest` case covers set, clear-to-null, unknown
  key, wrong type, unknown attribute, primary-key and null attribute name.
- Single processor: `FoundgineAotProcessor` was byte-identical in `foundgine-providers` and `foundgine-aot-generator`.
  The duplicate module is removed (reactor module, publish exclusion, docs). The fuller processor test (ORM maps and
  generated JPA models) replaced the older one in `foundgine-providers`; the generator README moved to
  `foundgine-providers/.../aot/README.md`. Samples already used `foundgine-providers` on the processor path.
- `jakarta.persistence-api` in `foundgine-providers` is no longer `optional`, matching C# where EF Core is a hard
  reference. Hibernate stays test-scope only.
- Added `ProviderPackageContentsTest` (ORM core, JPA provider, processor registered exactly once). It checks the
  classpath, not the jar: also run `jar tf foundgine-providers-*.jar | grep -E "storage/orm/jpa/FoundgineOrm|META-INF/services"`.
- Tutorials: the starter tutorial's JPA section (7c) and `Main` listing now match `SupplyChainOrm`, `SupplyChainJpa`
  and `Main`; `Foundgine-SupplyChain-Explained.md` sections 7 and 8 describe the generated maps/models and
  `FoundgineOrm`'s reader. The advanced tutorial contains no JPA listings, so it is unchanged. The stale
  `{@link SupplyChainJpa}` Javadoc in `SupplyChainSemantics` was corrected.
- C# comparison of the typed repository: the public surface matches `FoundgineEfRepository` (get, list, exists,
  count, add, update, single-attribute update, map update, delete). One behavioural difference: C#
  `UpdateAsync(id, values)` returns the `SaveChanges` row count, so it can return 0 when the new values equal the old
  ones, while Java returns 1 whenever the row exists. Java also rejects primary-key and unknown-attribute updates up front.

### Release 3.3.2

- `<revision>` in `java/pom.xml` is now 3.3.2 (every module inherits it); the generated `.flattened-pom.xml` files were
  updated to match and are regenerated by the build.
- Contents: single-attribute `update` via `Collections.singletonMap`, one `FoundgineAotProcessor` (the
  `foundgine-aot-generator` module was removed), `jakarta.persistence-api` no longer optional in `foundgine-providers`,
  and `ProviderPackageContentsTest`.
