# Supply Chain semantic boundary

This sample deliberately separates four concerns:

- **Metadata** — structural truth: entities, fields, keys, storage names and direct relationships.
- **Semantics** — application meaning: logical traversals such as `Product.shipments`.
- **Authorization** — who may exercise the discovered semantic surface.
- **Intent** — what the caller asks Foundgine to do.

`SupplyChainSemanticModel` starts from `SemanticModelBuilder.FromMetadata(...)`. It must not recreate structural entities or relationship IDs.

Generated numeric identities remain internal metadata implementation details. Application semantic configuration uses logical names and resolves them against the discovered graph.


## Security proving-ground traversals

The sample exposes two application-level logical traversals:

- `Product.shipments` — `purchaseOrderLines -> purchaseOrder -> shipments`.
- `Product.supplierIncidents` — `purchaseOrderLines -> purchaseOrder -> supplier -> incidents`.

The second traversal is intentionally adversarial: capability discovery must suppress it when any intermediate entity or relationship is not readable. This demonstrates that logical traversal discovery is a security-aware projection of the complete semantic path, not a shortcut around authorization.
