# Foundgine Supply Chain — Advanced Sample (Java)

The Java counterpart to
[`Foundgine.SupplyChain.Advanced`](../../../csharp/samples/Foundgine.SupplyChain.Advanced/README.md).
It is the Java Advanced sample for the fluent-configured semantic model →
tenant/role authorization → Foundgine's mutation engine → a minimal JSON-RPC MCP server → a
PostgreSQL-backed high-assurance `place_order` / `cancel_order` boundary.

> **New here?** This README covers _how to run it_. For the file-by-file "why" — mirroring the C#
> sample's numbered `docs/` set — see
> [`SupplyChain-Advanced-Tutorial.md`](./SupplyChain-Advanced-Tutorial.md).

> **Verification status (3.4.0 source):** the latest reported Maven run failed in the Advanced
> sample test phase. Fixes for semantic discovery, physical-column expectations and the catalog
> count are present in this checkpoint, but have not yet been rerun. Treat this sample as
> implemented-unverified until the Java 27 Maven and PostgreSQL gates pass.

## Flow

```
AI agent / MCP client
        │  JSON-RPC tools/call over HTTP
        ▼
  AdvancedMcpServer (/mcp)
        │
        ▼
  AdvancedMcpFacade (transport-neutral: decodes JSON into semantic commands)
        │
        ▼
  AdvancedMutationPipeline (Foundgine semantic mutation engine)
        │            ↳ SupplyChainAuthorization (tenant + role policy)
        │            ↳ SupplyChainSemanticModel (entities, relationships, weighted aliases)
        ▼
  PlaceOrderService / CancelOrderService — or —
  PostgresSupplyChainStore (real transactions, row locks, idempotency)
```

`Main` is the in-memory smoke-test entry point over the `SupplyChainData` fixture: it exercises a
claims-based scope reduction, recursive supplier-risk / fulfillment scenarios, order placement and
cancellation with idempotency-key replay. Run it to execute that path; the latest Maven verification
status is recorded above.

## What this demonstrates, package by package

| Package         | C# equivalent                                                   | Role                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| --------------- | --------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `model`         | fluent configuration                                            | `SupplyChainFoundgineModel` registers the semantic, persistence, entity-map and connection configurations that produce the metadata registry. `SupplyChainModelConnections` adds a model-to-entity connection (`PurchaseOrder` to `Supplier`) with a field map and a same-tenant authorization predicate; a type conversion is a third argument to `maps(...)`, e.g. `.maps(Contract::code, ContractRow::codeNumber, PARSE)`. |
| `domain`        | `Semantic/Domain`                                               | The application model as annotation-free `record`s. |
| `semantics`     | `Semantic/Semantics`                                            | `SupplyChainSemanticModel` builds the Foundgine `SemanticModel` from the domain, including weighted alias evidence (`Vendor`/`Seller` for `Supplier`, `PO`/`POs`/`Buy`/`Buys` for `PurchaseOrder`) and the `PlaceOrderCommand`/`CancelOrderCommand` mutation entities.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `authorization` | `Semantic/Authorization`                                        | `SupplyChainAuthorization` wires tenant/role rules into a `ConfiguredSemanticAuthorizationPolicy` — entity, field, relationship, predicate, and named-operation rules. `Claims` validates untrusted client-supplied claims (scope, warehouse, max rows, change ticket, expiry) and rejects reserved/hostile keys outright.                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `mutation`      | `Semantic/Application` + `MCP.Foundgine`                        | `AdvancedMutationPipeline` drives Foundgine's `FoundgineMutationEngine` end to end (intent → plan → authorization → ExecutionIR → provider). `PlaceOrderService`/`CancelOrderService` are the in-memory high-assurance domain services; `AdvancedMcpFacade` is the transport-neutral JSON boundary MCP calls into.                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `postgres`      | `MCP.Foundgine` (SQL boundary)                                  | `PostgresSupplyChainStore` owns the physical transaction, advisory locks, server-side pricing, and exact inventory-lot allocation for `place_order`/`cancel_order` against real PostgreSQL.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `mcp`           | `MCP.Foundgine/Program.cs` + `Semantic/McpClient`               | `AdvancedMcpServer` is a dependency-free JSON-RPC-over-HTTP MCP server (JDK `HttpServer`, no extra runtime dependency beyond Jackson) exposing `foundgine_query`/`foundgine_mutation`. `AdvancedMcpClientDemo` is a raw-HTTP MCP client that calls it, no SDK required.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `candidate-search demo` | `MCP.Foundgine/Program.cs` (`find_top_supplier_overdue_orders`) | `TopSupplierOverdueOrdersService` is a direct PostgreSQL candidate-search demo: it ranks exact, `pg_trgm`, full-text and optional `pg_search` matches and returns `resolved` / `clarification_needed` / `not_found`. It is hand-registered as a fourth MCP tool and queries PostgreSQL directly; it is **not** wired through the Java core `SemanticLexicalResolver` or its `GroundingDecision` pipeline. Its `plan` field is a stable hash of the fixed Supplier read shape, not a full `ExecutionIRCompiler`-derived fingerprint — see the class Javadoc. |
| `scenarios`     | `Semantic/Tests` (adversarial cases)                            | `Scenarios`/`ScenariosAdversarialInvariants` — recursive BOM/supplier-risk traversal with cycle detection, and fulfillment-shortage calculation, run against the live semantic model and authorization context rather than a disconnected fixture.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |

## Run

Start PostgreSQL:

```bash
cd src/java/samples/foundgine-supply-chain-advanced
docker compose up -d postgres
```

Compile and run the in-memory smoke test (`Main`):

```bash
cd src/java
mvn -pl samples/foundgine-supply-chain-advanced -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.advanced.Main
```

Run the MCP server:

```bash
mvn -pl samples/foundgine-supply-chain-advanced -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.advanced.mcp.AdvancedMcpServer
```

The server listens on `http://localhost:4783/mcp` (health check at `/health`). In a second
terminal, run the client demo against it:

```bash
mvn -pl samples/foundgine-supply-chain-advanced -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.advanced.mcp.AdvancedMcpClientDemo
```

## Actors and authorization

Authorization is tenant- and role-scoped (`CUSTOMER`, `ANALYST`, `WAREHOUSE_OPERATOR`,
`SUPPLY_CHAIN_MANAGER`) rather than the C# sample's five named actors (Alice/Bob/Carol/Dave/Admin),
but it enforces the same principles: entity/field/relationship/predicate rules run before planning,
`Supplier.RiskScore` and `InventoryLot.Quarantined` are restricted to roles that need them, and
client-supplied claims are validated and can only ever _narrow_ an identity's access
(`Authorization.applyClaims`), never expand it — a claim trying to smuggle in `role` or `isAdmin` is
rejected as a spoofing attempt before it reaches the policy at all.

## High-assurance mutation

`place_order` and `cancel_order` implement the same guarantees as the C# `PlaceOrder`/`CancelOrder`:
actor/tenant/ownership checks, positive-quantity validation, inventory availability across
authorized warehouses, server-side price calculation, atomic order + line + inventory-lot updates,
and idempotency-key replay protection — implemented in both the in-memory
(`PlaceOrderService`/`CancelOrderService`, used by `Main` and the MCP server) and PostgreSQL
(`PostgresSupplyChainStore`) paths. The PostgreSQL store additionally takes a
`pg_advisory_xact_lock` on the idempotency key so concurrent replays of the same key serialize
instead of racing. Runtime verification remains pending as noted above.

## Where to go next

- [Step-by-step tutorial](./SupplyChain-Advanced-Tutorial.md) — build every piece above from
  scratch, in order.
- [`../foundgine-supply-chain`](../foundgine-supply-chain/README.md) — the smaller starter this
  sample builds on conceptually.
- [C# `Foundgine.SupplyChain.Advanced`](../../../csharp/samples/Foundgine.SupplyChain.Advanced/README.md)
  — the reference this sample keeps parity with, including the ambiguity-resolution
  (`find_top_supplier_overdue_orders`) and weighted-alias-evidence features not yet ported to this
  module.

## Fluent ORM path (Step 72 — implemented-unverified)

The advanced JPA path runs on `FoundgineOrm` (`jpa/AdvancedJpaOrm`). The semantic metadata comes from the fluent
`SupplyChainFoundgineModel`; the annotation processor (configured in this module's `pom.xml`) generates
`GeneratedOrmMaps` and `GeneratedJpaModels` from the `@FoundgineOrmEntity` persistence classes (`WarehouseEntity`,
`InventoryLotEntity`, `ProductEntity`) into `com.foundgine.samples.supplychain.advanced.generated`.

### Method-reference reads

```java
var reader = new AdvancedReader(orm.reader(em), caller);
var rows = reader.from(Warehouse.class).select(Warehouse::id, Warehouse::name).rows();
```

Only getter/record-accessor method references are accepted (Java lambdas expose no expression trees); every read still
goes through authorize -> plan -> ExecutionIR -> JPA.

### CRUD

`application/AdvancedCrud` gives create/read/update/delete for Warehouse, InventoryLot and Product through the generic
`JpaEntities` gateway. Reads are gated by Foundgine. Writes need `Authorization.canWrite` plus scope rules: warehouses
are stamped with the caller's tenant and never change tenant; lots are checked through their (allowed, tenant-visible)
warehouse and are never listed without one; products are shared catalogue data. Known limit: `AdvancedJpaPolicy` still
lets a root read of `InventoryLot` through (it has no tenant column), so use `AdvancedCrud.listLots` rather than raw
lot reads.

### Migrations and schema ownership

The Advanced sample still uses the handwritten Flyway baseline
(`src/main/resources/db/migration/V1__supply_chain_advanced.sql`) and repeatable demo data
(`src/main/resources/db/demo/R__demo_data.sql`). The new `foundgine-migrations` generator is
available in this reactor, but the Advanced fluent model is **not yet a safe schema source**:
its configured persistence tables and semantic entities do not describe the physical PostgreSQL
schema consistently. Generating a migration from it now would create a second, incorrect schema.

The first reconciliation increment now maps the three JPA-backed entities (`Product`, `Warehouse`,
`InventoryLot`) to their exact V1 column names, SQL types, required-column semantics and existing
indexes. `AdvancedSchemaModelParityTest` checks this subset through `SchemaBuilder`. This is not full
V1 coverage: customer/order transaction tables, idempotency tables, supplier/purchase-order tables,
foreign keys, CHECK constraints, defaults, identity behavior and the `pg_trgm` extension remain
outside the generated model. See [Advanced schema audit and migration plan](./ADVANCED-SCHEMA-AUDIT.md)
for the exact boundary and next steps.

`AdvancedDatabase` continues to apply Flyway migrations on start
(`SUPPLYCHAIN_MIGRATE_ON_START`, default true; demo rows with `SUPPLYCHAIN_SEED_DEMO`, default
true). Hibernate remains `hbm2ddl.auto=validate`. Do not replace or baseline the existing V1
until the fluent model, JPA entities, direct PostgreSQL SQL, and constraints have a single agreed
physical schema.


### Schema reconciliation status

The Java fluent model now includes explicit physical persistence rows for the transactional V1 tables and the ambiguity-resolution tables `suppliers` and `purchase_orders`. Their column names, SQL types, nullability, keys and supported indexes are checked by `AdvancedSchemaModelParityTest`. The PostgreSQL `pg_trgm` GIN/operator-class index, identity generation, foreign keys, CHECK constraints and defaults remain owned by the unchanged handwritten V1. Generated migrations are not yet enabled for this sample; see [`ADVANCED-SCHEMA-AUDIT.md`](ADVANCED-SCHEMA-AUDIT.md).
