# Foundgine Supply Chain — Advanced Sample (Java)

The Java counterpart to
[`Foundgine.SupplyChain.Advanced`](../../../csharp/samples/Foundgine.SupplyChain.Advanced/README.md).
It is the fully wired Java proving ground: annotation-processor-generated semantic model →
tenant/role authorization → Foundgine's mutation engine → a minimal JSON-RPC MCP server → a
PostgreSQL-backed high-assurance `place_order` / `cancel_order` boundary.

> **New here?** This README covers _how to run it_. For the file-by-file "why" — mirroring the C#
> sample's numbered `docs/` set — see
> [`SupplyChain-Advanced-Tutorial.md`](./SupplyChain-Advanced-Tutorial.md).

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

`Main` runs the whole path once against the in-memory `SupplyChainData` fixture as a
deterministic smoke test: it validates a claims-based scope reduction, runs the recursive
supplier-risk / fulfillment scenarios, places an order (and replays the same idempotency key to
prove replay-safety), then cancels it (and replays that too).

## What this demonstrates, package by package

| Package         | C# equivalent                                                   | Role                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| --------------- | --------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `domain`        | `Semantic/Domain`                                               | The application model as `record`s, annotated with `@FoundgineEntity`, `@FoundgineRelationship`, `@FoundgineSemanticDimension`, and `@FoundgineAlias`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `semantics`     | `Semantic/Semantics`                                            | `SupplyChainSemanticModel` builds the Foundgine `SemanticModel` from the domain, including weighted alias evidence (`Vendor`/`Seller` for `Supplier`, `PO`/`POs`/`Buy`/`Buys` for `PurchaseOrder`) and the `PlaceOrderCommand`/`CancelOrderCommand` mutation entities.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `authorization` | `Semantic/Authorization`                                        | `SupplyChainAuthorization` wires tenant/role rules into a `ConfiguredSemanticAuthorizationPolicy` — entity, field, relationship, predicate, and named-operation rules. `Claims` validates untrusted client-supplied claims (scope, warehouse, max rows, change ticket, expiry) and rejects reserved/hostile keys outright.                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `mutation`      | `Semantic/Application` + `MCP.Foundgine`                        | `AdvancedMutationPipeline` drives Foundgine's `FoundgineMutationEngine` end to end (intent → plan → authorization → ExecutionIR → provider). `PlaceOrderService`/`CancelOrderService` are the in-memory high-assurance domain services; `AdvancedMcpFacade` is the transport-neutral JSON boundary MCP calls into.                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `postgres`      | `MCP.Foundgine` (SQL boundary)                                  | `PostgresSupplyChainStore` owns the physical transaction, advisory locks, server-side pricing, and exact inventory-lot allocation for `place_order`/`cancel_order` against real PostgreSQL.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `mcp`           | `MCP.Foundgine/Program.cs` + `Semantic/McpClient`               | `AdvancedMcpServer` is a dependency-free JSON-RPC-over-HTTP MCP server (JDK `HttpServer`, no extra runtime dependency beyond Jackson) exposing `foundgine_query`/`foundgine_mutation`. `AdvancedMcpClientDemo` is a raw-HTTP MCP client that calls it, no SDK required.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `ambiguity`     | `MCP.Foundgine/Program.cs` (`find_top_supplier_overdue_orders`) | `TopSupplierOverdueOrdersService` is the ambiguity-resolution demo capability: "top supplier in a state" is resolved through ranked candidates + evidence (exact match → pg_trgm fuzzy → full-text → optional pg_search, in that order), returning `resolved`/`clarification_needed`/`not_found` rather than guessing. Hand-registered as a fourth MCP tool in `AdvancedMcpServer`, talking directly to PostgreSQL (schema in `database/schema.sql`/`seed.sql`) via `FOUNDGINE_POSTGRES_CONNECTION_STRING`, independent of the in-memory `SupplyChainData` the rest of the sample runs against. Its `plan` field is a stable hash of the fixed Supplier read shape, not a full `ExecutionIRCompiler`-derived fingerprint like the C# original — see the class Javadoc. |
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
and idempotency-key replay protection — proven twice over: once in-memory
(`PlaceOrderService`/`CancelOrderService`, used by `Main` and the MCP server) and once against real
PostgreSQL (`PostgresSupplyChainStore`), which additionally takes a `pg_advisory_xact_lock` on the
idempotency key so concurrent replays of the same key serialize instead of racing.

## Where to go next

- [Step-by-step tutorial](./SupplyChain-Advanced-Tutorial.md) — build every piece above from
  scratch, in order.
- [`../foundgine-supply-chain`](../foundgine-supply-chain/README.md) — the smaller starter this
  sample builds on conceptually.
- [C# `Foundgine.SupplyChain.Advanced`](../../../csharp/samples/Foundgine.SupplyChain.Advanced/README.md)
  — the reference this sample keeps parity with, including the ambiguity-resolution
  (`find_top_supplier_overdue_orders`) and weighted-alias-evidence features not yet ported to this
  module.
