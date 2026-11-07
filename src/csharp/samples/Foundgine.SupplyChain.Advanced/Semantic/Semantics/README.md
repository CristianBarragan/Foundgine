# Supply Chain semantic boundary

## Where each concern is configured

The domain types in `Domain/Domain.cs` are plain records with no Foundgine reference. Everything Foundgine needs to know is configured fluently in `Semantics/Configuration` and registered once in `SupplyChainModel`:

| Concern | File |
| --- | --- |
| Entity names, aliases, dimensions, events, logical relationships | `SupplyChainModelConfigurations.cs` |
| Tables, keys, indexes | `SupplyChainPersistenceConfigurations.cs` |
| Model member to storage member | `SupplyChainEntityMaps.cs` |
| Relationship to physical foreign key | `SupplyChainConnections.cs` |
| Model-to-entity connection, field maps, conversions, authorization | `SupplyChainModelConnections.cs` |
| Registration | `SupplyChainModel.cs` |

`new SupplyChainModel().Build()` validates the whole set and returns the metadata registry that `SupplyChainSemanticModel` starts from. Identities are now derived from names (entity and field ids from semantic names, column ids from table and column name), so the explicit numeric ids the attributes used to carry are gone.

`SupplyChainModelConnections.cs` shows the model-to-entity side: `PurchaseOrder.SupplierId` connects to the `Supplier` entity, maps `SupplierId` to `Id`, and carries a tenant authorization predicate (`(context, supplier) => context.TenantId == supplier.TenantId`) that is read from the expression tree. A type conversion is a third argument to `Maps(...)`, for example `.Maps(x => x.Code, e => e.CodeNumber, int.Parse)`; see `Foundgine.Core/Semantic/Configuration/README.md`.

`Product` and `ProductComponent` are related three ways (`components`, `usedInComponents`, `componentProduct`), so the two connections between them name the navigation they configure.

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
