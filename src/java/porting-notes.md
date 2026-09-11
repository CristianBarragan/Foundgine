
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
The Java port now mirrors the C# malicious MCP/JSON intent invariants at `JsonReadIntentAdapter`: unknown security/provider authority properties are rejected, tenant-like filters do not populate trusted security context, and nested predicate structures are bounded before planning.

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
