# Java test parity tracker

The project is being aligned to the C# test suite by behavior and boundary, not by mechanically translating syntax.

| C# test project | C# test files | Java status |
|---|---:|---|
| Foundgine.Aot.Tests | 3 | Partial: generator identity coverage exists |
| Foundgine.E2E.Tests | 67 | In progress: Advanced SupplyChain integration path started |
| Foundgine.GraphQL.HotChocolate.Tests | 26 | Transport-specific; Java parity will use Java transport adapters rather than Hot Chocolate |
| Foundgine.HighAssurance.Tests | 1 | Partial: Advanced high-assurance mutations covered |
| Foundgine.InMemory.Tests | 3 | Partial: provider execution coverage exists; broader parity pending |
| Foundgine.Intent.Json.Tests | 4 | Partial: intent types/serialization coverage exists |
| Foundgine.MCP.Tests | 8 | Partial: Advanced MCP facade integration coverage exists |
| Foundgine.Planning.Tests | 38 | Partial: planner/rewrite core coverage exists |
| Foundgine.Postgres.Vector.Tests | 3 | Pending PostgreSQL/vector parity |
| Foundgine.Security.Authority.Tests | 17 | Pending broader authority/security parity |
| Foundgine.Security.Tests | 23 | Pending broader adversarial parity |
| Foundgine.Semantics.Tests | 52 | In progress: identity, aliases, grounding, mutation, planning, model lifecycle and query contracts mirrored |
| Foundgine.Sql.Tests | 1 | Partial: SQL mutation normalization coverage exists |

The Java suite currently contains **87 test classes**. New parity tests are named `*ParityTest` where the purpose is to make the C# source test being mirrored explicit.

## Porting order

1. Core semantic/unit tests
2. Core planning and execution tests
3. Runtime unit tests and mutation security tests
4. Provider unit/conformance tests
5. Advanced SupplyChain integration tests
6. MCP integration/security tests
7. PostgreSQL integration tests
8. AOT generator/runtime parity tests
9. End-to-end and red-team suites
10. Benchmarks and deployment/pipeline integration

## Latest parity pass
- `Foundgine.Semantics.Tests/SemanticIdentityTests.cs` -> `SemanticIdentityParityTest`
- `Foundgine.Semantics.Tests/SemanticQueryOptionsTests.cs` -> `SemanticQueryOptionsParityTest`
- `Foundgine.Semantics.Tests/SemanticRelationshipFilterTests.cs` -> `SemanticRelationshipFilterParityTest`
- `Foundgine.Semantics.Tests/SemanticContractAttestationTests.cs` -> `SemanticContractAttestationParityTest`

- `Foundgine.Planning.Tests/Security/PlanSecurityInvariantTests.cs` -> `PlanSecurityInvariantParityTest`
- `Foundgine.Security.Tests/Penetration/WarrantTrustBoundaryPenetrationTests.cs` -> `WarrantTrustBoundaryParityTest`
- `Foundgine.MCP.Tests/McpBoundaryTests.cs` -> `McpBoundaryParityTest`
- `Foundgine.Aot.Tests/IdentityDeterminismTests.cs` -> `GeneratorSemanticIdentityParityTest`

### Latest parity pass

- `Foundgine.Security.Tests/Penetration/SemanticBoundaryPenetrationTests.cs` → `SemanticBoundaryPenetrationParityTest` — denied entities/fields/relationships, authorization predicate preservation, and fingerprint isolation.
- `Foundgine.Security.Tests/Penetration/MutationAuthorizationPenetrationTests.cs` → `MutationAuthorizationPenetrationParityTest` — unauthorized write/return/filter fields and all-or-nothing batch authorization.
- `Foundgine.Security.Tests/Security/ProviderExecutableConformanceTests.cs` + `ProviderExecutableConformanceGateTests.cs` → `ProviderExecutableConformanceParityTest` — concrete evaluator requirement, proof completeness, exact plan/IR binding, provider identity, and provider-plan conformance violations.
- `Foundgine.Security.Tests/Security/ProviderSecurityConformanceMatrixTests.cs` → `ProviderSecurityConformanceMatrixParityTest` — baseline provider guarantees, high-assurance PostgreSQL profile, unknown-provider fail-closed behavior, and hostile provider claims.
- `Foundgine.Semantics.Tests/SemanticIdentityTests.cs` -> `SemanticIdentityParityTest`
- `Foundgine.Semantics.Tests/SemanticQueryOptionsTests.cs` -> `SemanticQueryOptionsParityTest`
- `Foundgine.Semantics.Tests/SemanticRelationshipFilterTests.cs` -> `SemanticRelationshipFilterParityTest`
- `Foundgine.Semantics.Tests/SemanticContractAttestationTests.cs` -> `SemanticContractAttestationParityTest`

- `Foundgine.Semantics.Tests/SemanticAlgebraTests.cs` → `SemanticAlgebraParityTest`
- `Foundgine.Semantics.Tests/SemanticOperationGraphSafetyStep32Tests.cs` → `SemanticOperationGraphSafetyParityTest`
- `Foundgine.Semantics.Tests` authorization-composition invariant → `SemanticAuthorizationCompositionParityTest`

### Latest parity pass
- `Foundgine.Semantics.Tests/SemanticIdentityTests.cs` -> `SemanticIdentityParityTest`
- `Foundgine.Semantics.Tests/SemanticQueryOptionsTests.cs` -> `SemanticQueryOptionsParityTest`
- `Foundgine.Semantics.Tests/SemanticRelationshipFilterTests.cs` -> `SemanticRelationshipFilterParityTest`
- `Foundgine.Semantics.Tests/SemanticContractAttestationTests.cs` -> `SemanticContractAttestationParityTest`


- `SemanticApproximateRetrievalParityTest` — provider-neutral approximate retrieval, entity filtering, deterministic ordering/limits, request bounds.
- `SemanticReferenceGroundingParityTest` — relationship grounding, fail-closed traversal, ambiguity preservation.
- `SemanticLexicalResolverParityTest` — token budget refusal, competing meanings, cancellation fail-closed behavior.
- `SemanticMutationPlannerParityTest` — dependency ordering and returned-field validation.


### Latest parity pass
- `Foundgine.Semantics.Tests/SemanticIdentityTests.cs` -> `SemanticIdentityParityTest`
- `Foundgine.Semantics.Tests/SemanticQueryOptionsTests.cs` -> `SemanticQueryOptionsParityTest`
- `Foundgine.Semantics.Tests/SemanticRelationshipFilterTests.cs` -> `SemanticRelationshipFilterParityTest`
- `Foundgine.Semantics.Tests/SemanticContractAttestationTests.cs` -> `SemanticContractAttestationParityTest`


- `Foundgine.Security.Tests/Security/MutationExecutionSecurityGateTests.cs` / authorization revalidation invariants → `SecurityExecutionAuthorizationRevalidationParityTest`
- `Foundgine.Security.Tests/Penetration/WarrantTrustBoundaryPenetrationTests.cs` / replay and revocation invariants → `WarrantReplayAndRevocationParityTest`
- `Foundgine.Security.Tests/AdversarialAgentBoundaryTests.cs` / capability composition invariants → `SecurityCapabilityCompositionParityTest`

- Added provider parity coverage for InMemory provider plan boundaries and MCP mutation/security transport boundaries.

### Latest parity pass
- `Foundgine.Semantics.Tests/SemanticIdentityTests.cs` -> `SemanticIdentityParityTest`
- `Foundgine.Semantics.Tests/SemanticQueryOptionsTests.cs` -> `SemanticQueryOptionsParityTest`
- `Foundgine.Semantics.Tests/SemanticRelationshipFilterTests.cs` -> `SemanticRelationshipFilterParityTest`
- `Foundgine.Semantics.Tests/SemanticContractAttestationTests.cs` -> `SemanticContractAttestationParityTest`

- Added malicious JSON intent boundary tests mirroring C# `McpMaliciousIntentSecurityTests` at the Java serialization boundary.
- Covers security/provider authority injection, hostile tenant filters remaining untrusted input, and bounded malicious predicate structures.


### Latest E2E execution parity
- `ExecutionIRParityE2ETest` mirrors semantic-plan → execution-IR topology and authorization-boundary tests.
- `ExecutionMutationIRParityE2ETest` mirrors canonical mutation IR lowering and forward-dependency rejection.
- `ExecutionSecurityBoundaryParityE2ETest` mirrors security attestation and deterministic execution-evidence tests.

### Runtime/E2E approval parity
- `PlanApprovalParityE2ETest` mirrors approval execution, semantic-version invalidation, and plan-fingerprint invalidation from `Foundgine.E2E.Tests/PlanApprovalTests.cs`.

### Latest Advanced SupplyChain high-assurance parity
- Added idempotency request binding for PlaceOrder and CancelOrder so a reused key cannot replay a different request.
- Added concurrent same-key PlaceOrder coverage proving a single logical mutation is committed.
- Added replay authorization coverage so current write authorization is still required.
- Mirrored the same idempotency binding into the PostgreSQL mutation boundary and schema.

## Latest high-assurance mutation parity

The Advanced SupplyChain suite now mirrors additional C# high-assurance invariants: duplicate-line normalization, execution-boundary authorization with no state change, cancellation state transitions, and restoration of the exact allocated inventory lot.

### High Assurance Banking

Mirrored the C# `Foundgine.HighAssurance.Tests/TransferFundsTests` into the Java benchmark fixture, including available-funds semantics, frozen destination rejection, daily limits, tenant isolation, execution-time authorization, idempotent replay, key/request binding, and concurrent same-key execution.

## Continued parity pass — execution/result boundaries

Added parity coverage for:
- execution result materialization and duplicate-row collapse
- null root/child identity handling during result materialization
- nested mutation result cardinality and batch-result accounting
- connection traversal lowering and root parent-edge rejection

### Latest parity pass
- Authorization preservation proof: preserved/dropped invariant behavior.
- Security preservation proof: invariant ordering and unknown-invariant fail-closed behavior.
- Semantic query pagination: cursor/offset/limit validation.
- Semantic plan fingerprint: exact pagination values versus shape-key parameterization.


### Latest planning parity additions
- `Foundgine.Planning.Tests/PredicatePushdownRuleTests.cs` → `PredicatePushdownRuleParityTest` — bounded AND/OR distribution, semantic-equivalence proof, security-invariant preservation, and expansion budget.
- `Foundgine.Planning.Tests/ConnectionPlanningTests.cs` → `ConnectionPlanningParityTest` — connection traversal lowering, root-edge rejection, edge separation, and authorization preservation.
- `Foundgine.Planning.Tests/ExecutionAlgebraInvariantTests.cs` → `ExecutionAlgebraInvariantParityTest` — structural execution operations, query-clause preservation, and logical relationship/connection traversal.
- `Foundgine.Planning.Tests/SemanticPlanAuthorizationBindingProofTests.cs` → `SemanticPlanAuthorizationBindingProofParityTest` — binding preservation/replacement rejection and unbound-plan optimization.
- `Foundgine.Planning.Tests/SemanticEquivalenceProofTests.cs` → `SemanticEquivalenceProofParityTest` — optimizer proof, authorization commutativity, meaningful field/pagination rejection, and security-contract rejection.
- `Foundgine.Planning.Tests/ProviderAwareCostSelectionTests.cs` → `ProviderAwareCostSelectionParityTest` — provider-aware rule ranking, proof-gated advisory cost, and selection-history provenance.

- Aggregate rewrite/cardinality semantics
- Relationship aggregate-filter pushdown
- Rewrite-rule composer budgets and deterministic application
- Authorization predicate canonicalization

## Semantic parity pass — 2026-09-12

Added Java parity coverage for the next semantic boundary batch:

- `SemanticArchitectureHardeningParityTest`
- `SemanticAuthorizationParityTest`
- `SemanticOperationGraphAuthorizationParityTest`
- `SemanticRequestResolverParityTest`
- `SemanticResolutionBoundaryParityTest`
- `MetadataToSemanticsConfigurationParityTest`
- `OpenIntentTraversalParityTest`
- existing aggregate capability/rewrite and lexicon projection parity tests from the previous pass remain included

The new tests cover contract-bound authorization, fail-closed IR validation, relationship/field filtering, canonical request resolution, logical traversal expansion, traversal-to-operation convergence, metadata discovery, multiple-root loose validation, and request boundary rejection.

The sources were syntax-compiled against the existing Java module classes with lightweight JUnit API stubs because Maven is unavailable in the execution environment and external network access is disabled. This is **not** a substitute for a real Maven/Surefire run.

## Continued security penetration pass — 2026-09-12
- `Foundgine.Semantics.Tests/AliasWeightEvidenceGateTests.cs` → `AliasWeightEvidenceGateParityTest`: weighted lexical evidence scoping, provenance, strongest evidence, unweighted aliases, boundaries, and contract fingerprint binding.
- `Foundgine.Security.Tests/Penetration/IntentParserHardeningPenetrationTests.cs` → `IntentParserHardeningParityTest`: malformed selection/filter structures, negative pagination, unsupported provider-like filter kinds, empty boolean expressions, and bounded JSON value depth.
- `Foundgine.Security.Tests/Penetration/TransportAndSecretLeakagePenetrationTests.cs` → `TransportAndSecretLeakageParityTest`: duplicate/unknown authority properties, permissive transport behavior, and Unicode-confusable authority keys.

The remaining penetration suite is intentionally not represented by shallow placeholder tests. Cases that require planner/provider proof objects or PostgreSQL execution are being ported against their actual Java equivalents rather than weakening the assertion to make the test compile.

## Pass 4 — security penetration rails

- CryptographicAndIdentityPenetrationParityTest
- PlanIntegrityPenetrationParityTest
- SecurityProofRailsPenetrationParityTest

These ports target the Java security-warrant cryptographic boundary and the exact ProviderPlan/ExecutionIR security-proof binding. C# record mutation/replacement cases are adapted to Java's immutable records and identity-bound ProviderPlan instances.

## Pass 5 — resource/transport/cache penetration parity — 2026-09-12

Added Java parity coverage for:
- `GraphAndResourceDoSPenetrationTests`
- `JsonAndMcpBoundaryPenetrationTests`
- `ResourceExhaustionPenetrationTests`
- `CacheModelAndPredicatePenetrationTests`

These tests exercise nested selection/filter depth, selection fan-out, page-size/cursor limits, untrusted transport authority/provider/SQL properties, relaxed unknown-property handling, cache-key isolation, security-bearing plan fingerprint separation, and preservation of unknown security obligations.

## Security warrant parity — delegation state and chain

Added concrete parity coverage for `SecurityWarrantDelegationStateMachine` and `SecurityWarrantDelegationChainValidator`, including fail-closed revoked/compromised transitions, key-rotation sequencing, duplicate registration, root ancestry integrity, expected-root binding, and deterministic chain digest behavior.

## Provider parity step — in-memory execution — 2026-09-12

The Java in-memory provider now mirrors the C# provider execution surface rather than the
former minimal shell:

- `InMemoryRow`
- `InMemoryDataSet`
- `InMemoryPlan`
- `InMemoryCompiler`
- `InMemoryExecutionProvider`

Coverage now includes direct ExecutionIR execution, root filter/order/offset/limit behavior,
relationship traversal through metadata join keys, authorization predicates, projection/cell
boundaries, provider security-conformance evaluation, and fail-closed execution without
authorization provenance.

The Java test class `InMemoryProviderParityTest` contains the behavioral cases for this step.
Full Maven/Surefire execution is still pending in an environment with Maven and dependency
artifacts installed.

## PostgreSQL batched mutation provider parity — 2026-09-12

Added `PostgresBatchedMutationCompilerParityTest` for the concrete PostgreSQL
batch compiler. Coverage includes:

- ExecutionMutationIR and safe `tryCompile` entry points
- compiler-owned `__fg_corr` correlation for batched Create
- generated-identity Create without borrowing a user-visible key
- duplicate literal Upsert key fallback
- dependency-level grouping and cross-level correlation metadata

The Java provider now also contains `PostgresBatchedMutationExecutionProvider`,
which mirrors the C# single-statement execution boundary, result correlation,
fail-closed result validation, typed PostgreSQL array binding, and sequential
fallback behavior.

Full PostgreSQL/JDBC E2E execution remains pending in an environment with Maven,
PostgreSQL JDBC artifacts, and a running PostgreSQL instance.


### PostgreSQL physical execution parity — 2026-09-12
- `PostgresBatchedMutationExecutionPostgresE2ETest` — opt-in real PostgreSQL JDBC coverage for generated-identity propagation across dependency levels, caller-owned transaction rollback, provider-owned transaction commit, and cancellation before execution.
- `CancellationTokenParityTest` — live cancellation callback registration, immediate registration after cancellation, linked-source propagation, and registration disposal.


## PostgreSQL vector provider parity — 2026-09-12

Ported the C# `Foundgine.Postgres.Vector.Tests` contract to Java in `PgVectorParityTest` and completed the corresponding provider surface:

- documented `PgVectorOptions` defaults and qualified identifier quoting
- distance-to-pgvector operator mapping (`<=>`, `<->`, `<#>`)
- bounded provider relevance scoring for cosine, L2, and inner-product distance
- HNSW operator-class mapping
- null-constructor guards and nullable-options defaults
- schema creation now includes kind and HNSW indexes
- lexical retrieval now respects effective candidate kinds, context hints, configured distance, schema-qualified table names, and vector SQL parameters
- indexed entries now preserve aliases and use PostgreSQL `vector` casts
- contract indexing validates embedding cardinality and rolls back failed transactions
- single-entry indexing is available as the Java equivalent of `IndexEntryAsync`

Real PostgreSQL execution remains opt-in and requires Maven dependencies plus a running PostgreSQL instance.

### AOT generator pass
- Added `foundgine-aot-generator` as the compile-time Java annotation processor.
- Generates deterministic `MetadataRegistry` source from `@FoundgineEntity` declarations.
- Mirrors the C# AOT boundary rather than replacing it with runtime reflection.
- SupplyChain and Advanced SupplyChain Maven builds are configured to invoke the processor.

### AOT consumption parity — 2026-09-12

Added `AotGeneratedMetadataIntegrationTest` to the Advanced Supply Chain sample. The test
loads the generated registry produced by `FoundgineAotProcessor` and verifies that the sample
actually consumes generated entity metadata, aliases, event metadata, and temporal metadata.
This closes the gap where the annotation processor was configured but its generated artifact was
not yet exercised by a sample-level test.

Also fixed the Java pgvector lexical candidate source's malformed escaped-newline declaration in
`EmbeddingGenerator`, which prevented clean Java compilation of the provider source.

### PostgreSQL/SQL provider continuation

- Added PostgreSQL retrieval strategy parity for Fuzzy, FullText, Search, GraphSimilarity and explicit Vector delegation.
- Added omitted-field fallback parity for approximate retrieval.
- Added JDBC physical placeholder normalization tests so named provider-plan placeholders do not leak into `PreparedStatement` execution.
- Added PostgreSQL retrieval-options parity tests against `Foundgine.Sql.Tests/PostgresRetrievalOptionsTests.cs`.

## SQL execution boundary parity — 2026-09-12

`SqlExecutionProvider` now mirrors the C# physical execution boundary more closely:

- live JDBC cancellation registration via `PreparedStatement.cancel()`;
- cancellation checks before bind, before execution, while materializing rows, and after page construction;
- explicit provider-owned transaction mode without changing the default caller-owned behavior;
- runtime pagination-limit handling and cursor page materialization remain preserved;
- execution evidence remains bound to the compiled SQL plan and authorization provenance.

Full Maven/Surefire and live PostgreSQL read-path execution remain environment-dependent.

## SQL compiler regression parity — 2026-09-12

Added `SqlCompilerParityTest` covering the C# SQL/E2E provider boundary:

- aggregate Count rendering and optimized Exists rendering;
- exact-count fallback behavior;
- relationship `SOME` and `ALL` correlated predicates;
- schema-qualified storage-name quoting;
- execution-context limit/offset parameters.

This complements the existing PostgreSQL retrieval, mutation, JDBC placeholder, and SQL execution
boundary tests. Compound cursor ordering/seek, aggregate ordering, authorization SQL rendering,
and live PostgreSQL read-path E2E remain next.

- `Foundgine.E2E.Tests` SQL cursor/authorization rendering contracts → `SqlCursorAuthorizationParityTest` — compound cursor tie-breaking, ascending/descending seek predicates, invalid cursor arity, authorization context parameterization, boolean predicate grouping, and cursor value conversion.

### Pass 20 — PostgreSQL read-path execution

Added `SqlExecutionPostgresE2ETest` for real PostgreSQL execution when `FOUNDGINE_POSTGRES_CONNECTION_STRING` is configured. Coverage includes authorized relationship filtering, parameter binding, result-cell materialization, execution evidence, cursor end-cursor round trips, and stable primary-key cursor ordering. The suite creates an isolated schema per test and is skipped when PostgreSQL is unavailable.

### Pass 21 — PostgreSQL nested relationship execution

- `Foundgine.Sql.Tests` / PostgreSQL provider parity: nested selected relationship projection, `SOME`, `NONE`, `ALL`, and relationship `COUNT` execution are now represented in Java real-PostgreSQL E2E coverage.
- Coverage verifies parameterized semantic values, root authorization, join materialization, and one-to-many result cardinality.

### Pass 22 — PostgreSQL aggregate ordering/cursor E2E

Added `realPostgres_aggregateCountOrdering_roundTripsCursorWithStableRootKeyTieBreak` to
`SqlExecutionPostgresE2ETest`. It verifies real PostgreSQL `COUNT(*)` relationship ordering,
root primary-key tie-breaking, authorization filtering, hidden aggregate cursor materialization,
and a second-page seek using the aggregate cursor value.


#### Pass 23 — PostgreSQL high-assurance mutation security

- Added parity coverage for the C# `PostgresMutationSecurityConformanceTests` contract semantics.
- Required invariants now include tenant isolation, runtime authorization, ownership, daily limit, atomic mutation, deterministic row locking, idempotency, replay protection, audit and execution receipt.
- Each removed obligation is required to fail closed.

### Pass 24 — PostgreSQL High-Assurance physical executor

Mirrored the C# `PostgresTransferFundsExecutor` physical boundary in Java: JDBC transaction ownership, advisory idempotency serialization, deterministic account row locks, authoritative authorization-context locking, execution-time authorization evidence binding/recheck, atomic mutation CTE, and rollback/fault-injection behavior. Added opt-in real PostgreSQL E2E tests for commit, replay, rollback, and authorization revocation.


### Pass 25 — High-Assurance PostgreSQL concurrency

`PostgresTransferFundsConcurrencyParityTest` now mirrors the C# `PostgresTransferFundsConcurrencyTests` and `PostgresTransferFundsVisibilityRaceTests`: same-key serialization, fault rollback handoff, opposing-transfer lock ordering, ownership/frozen/tenant/delete visibility races, and post-lock authorization revocation.

### Pass 26 — PostgreSQL batch mutation safety guards

Added compiler parity coverage for mixed mutation kinds on one entity, DELETE dependency-source rejection, and empty-batch rejection. These complement the existing generated-identity correlation, duplicate-upsert-key, dependency-level, cancellation, and real PostgreSQL batch execution tests.

### Pass 27 — authority and execution binding

- `AuthorizationContextIntegrityKeyLifecycleSecurityTests.cs` → `AuthorizationContextIntegrityKeyLifecycleParityTest` — Java runtime key lifecycle, replay/sequence protection, retirement safety, operator authorization, concurrent rotation and immutable snapshot publication.
- `AuthorizationExecutionBindingSecurityTests.cs` → `AuthorizationExecutionBindingParityTest` — exact request/evidence binding, mutation/resource/tenant/actor drift rejection, authorization version/fingerprint binding and denied-evidence rejection.
- C# `AuthorizationExecutionBinding` is now used by Java `PostgresTransferFundsExecutor` at the execution commit gate.

### Pass 28 — Extensions / CI

- Added structural parity for `Foundgine.Extensions` via `foundgine-extensions`.
- Added deterministic extension registry tests.
- Added Java 21 Maven CI workflow for the `java` branch and an opt-in PostgreSQL E2E job.
- GraphQL/HotChocolate remains transport-specific; it is not introduced into Core or Runtime.

### Pass 29 security audit

Added Java parity tests for the previously underrepresented C# security boundaries:

- authority-bearing plan/cache partition rails
- protocol-neutral semantic resource limits
- mutation resource limits
- mutation execution security certificate binding and provider conformance

The audit found these production mechanisms already existed in Java; the missing parity was primarily regression coverage. Remaining audit targets are the named PostgreSQL authority lifecycle/recovery fixtures and broader adversarial E2E composition.

- **MCP agent client**: added Java parity coverage for capability discovery, dynamic query execution, SSE result unwrapping, and JSON-RPC error handling.
