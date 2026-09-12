
## Advanced SupplyChain — high-assurance cancellation

The Java Advanced sample now includes `CancelOrderService`, mirroring the C# high-assurance cancellation boundary. Cancellation is tenant/ownership checked, limited to pending orders, restores the exact allocated inventory lot, commits atomically with rollback on failure, and supports deterministic idempotent replay/evidence.

## Advanced mutation runtime pipeline

The Advanced SupplyChain sample now demonstrates the full Java mutation boundary rather than calling the domain mutation services directly:

`semantic intent -> SemanticMutationPlanner -> MutationAuthorizer -> ExecutionMutationIR -> DomainMutationProvider -> PlaceOrderService / CancelOrderService -> MutationExecutionResult`

`PlaceOrderCommand` and `CancelOrderCommand` are explicit semantic command entities. This keeps command intent separate from the physical Order/Inventory storage model and demonstrates the Foundgine principle that semantic meaning is not physical execution.

## Test parity pass — security/planning/MCP/AOT
The Java suite now mirrors additional C# security and boundary contracts: plan security invariant derivation, fail-closed warrant trust/replay controls, MCP transport delegation, and deterministic AOT semantic identity behavior. Parity work should continue by translating behaviorally equivalent tests while excluding C#-only transport/framework tests.

## Continued semantic/planning test parity

Added four Java parity suites covering approximate retrieval, reference grounding, lexical grounding budgets/ambiguity/cancellation, and mutation dependency planning. Java test classes: 37.

## Test parity — hostile JSON intent boundary
The Java now mirrors the C# malicious MCP/JSON intent invariants at `JsonReadIntentAdapter`: unknown security/provider authority properties are rejected, tenant-like filters do not populate trusted security context, and nested predicate structures are bounded before planning.

## Test parity — Runtime approval boundary
The Java Runtime now mirrors the C# plan-approval E2E contract: an approved plan executes only when the semantic version set and current plan fingerprint still match; tampered approvals fail closed before provider execution.

## Advanced SupplyChain — high-assurance idempotency parity
Java PlaceOrder/CancelOrder idempotency keys are now cryptographically bound to the request shape. Reusing a key for a different request fails closed, concurrent PlaceOrder calls with the same key commit once, and replay still passes through current authorization checks. The PostgreSQL schema/store mirrors the same request-fingerprint contract.

## High-assurance mutation parity — latest

Added Java parity coverage for duplicate-line normalization, fail-closed cancellation authorization, cancelled-order state transitions, and exact inventory-lot restoration. These tests intentionally exercise the application mutation boundary rather than bypassing it with direct fixture mutation.

## Test parity — planning/query pagination

Added Java parity coverage for authorization/security preservation proofs and semantic query pagination. Exact plan fingerprints include pagination values while shape fingerprints intentionally parameterize them, matching the provider-plan caching contract.


## Test parity — planning rewrite pass
Mirrored aggregate cardinality, aggregate relationship-filter pushdown, rewrite-rule composition budgets, and authorization canonicalization behavior from the C# planning suite.

## Latest parity pass

- Added planning dependency-boundary coverage and enforced frozen-contract validation before contract-bound planning.
- Added semantic contract planning tests for unknown entities, fields, and relationship targets.
- Added mutation planning boundary tests for narrow schemas, unfiltered updates, and invalid return fields.
- Added proof-carrying rewrite-rule contract tests, including unknown security obligations failing closed.

\n## PostgreSQL vector provider parity — latest
The Java pgvector provider now mirrors the C# retrieval/indexing contract more closely. `PgVectorOptions` supports the documented defaults and safe qualified identifiers; `PgVectorSemanticLexicalCandidateSource` now maps all distance modes, converts distance into provider relevance, applies candidate-kind/context retrieval hints, and uses schema-qualified vector SQL. `PgVectorSemanticLexiconIndexClient` now creates the vector table and indexes, preserves aliases, validates embedding cardinality, rolls back failed contract reindexing, and supports single-entry indexing. `PgVectorParityTest` mirrors the provider-neutral unit contracts without requiring a live database.


### Pass 25 — PostgreSQL High-Assurance concurrency and visibility parity

Added opt-in Java PostgreSQL parity tests mirroring the C# HighAssurance concurrency/visibility suite:

- eight concurrent requests sharing one idempotency key serialize through `pg_advisory_xact_lock` and commit exactly once;
- opposing transfers acquire account locks in deterministic UUID order and complete without deadlock;
- a failed same-key transaction releases its advisory lock so a waiting request can execute the transfer exactly once;
- a failed opposing transfer releases row locks and rolls back completely so the waiting transfer can commit;
- ownership changes queued before transfer lock acquisition are visible to post-lock authorization;
- frozen-state changes committed before lock acquisition are observed by the transfer;
- tenant reassignment and account deletion races fail closed after authoritative row locking;
- authorization revocation while the transfer waits is observed by the execution-time authorization decision.

The tests remain opt-in via `FOUNDGINE_POSTGRES_CONNECTION_STRING` and are designed to exercise the same PostgreSQL `READ COMMITTED` lock/visibility model as the C# tests.

## Pass 29 — security parity audit: authority partitions and resource gates

- Audited the C# `Foundgine.Security.Tests` / `Foundgine.Security.Authority.Tests` surface against Java after the PostgreSQL work.
- Added `SecurityAuthorityPartitionRailsParityTest` covering subject, audience, tenant, resource scope, warrant digest, null-vs-explicit values, delimiter collision resistance, and deterministic reuse of the same authority context.
- Added `SecurityResourceLimitParityTest` covering non-JSON selection-depth limits, page-size limits, relationship order-path limits, filter-node limits, and cursor-length limits.
- Added `MutationSecurityResourceLimitParityTest` covering mutation count, fields per operation, return fields, and dependency-count limits.
- Added `MutationExecutionSecurityGateParityTest` covering exact IR/provider binding, provider-owned invariant evaluator requirements, and composition of upstream authorization evidence with provider conformance.
- Existing Java implementations already had the corresponding production rails; this pass closes the test-parity gap rather than introducing parallel security mechanisms.
- The remaining C#-only security fixtures should be treated as the next audit targets: transport/adversarial end-to-end composition and PostgreSQL authority lifecycle/recovery coverage where Java does not yet have a one-to-one named parity fixture.
