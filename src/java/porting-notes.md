
## Advanced SupplyChain — high-assurance cancellation

The Java Advanced sample now includes `CancelOrderService`, mirroring the C# high-assurance cancellation boundary. Cancellation is tenant/ownership checked, limited to pending orders, restores the exact allocated inventory lot, commits atomically with rollback on failure, and supports deterministic idempotent replay/evidence.

## Advanced mutation runtime pipeline

The Advanced SupplyChain sample now demonstrates the full Java mutation boundary rather than calling the domain mutation services directly:

`semantic intent -> SemanticMutationPlanner -> MutationAuthorizer -> ExecutionMutationIR -> DomainMutationProvider -> PlaceOrderService / CancelOrderService -> MutationExecutionResult`

`PlaceOrderCommand` and `CancelOrderCommand` are explicit semantic command entities. This keeps command intent separate from the physical Order/Inventory storage model and demonstrates the Foundgine principle that semantic meaning is not physical execution.
