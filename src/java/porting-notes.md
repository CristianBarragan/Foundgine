
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
