# Building the Foundgine Supply Chain "Advanced" Sample (Java) — Step by Step

This is the Java counterpart to the C# advanced sample's numbered `docs/` set
(`00-Overview-And-Setup.md` through `05-Adversarial-Security-Testing.md`). Rather than five
separate files, this single tutorial walks through the same five concerns in order, against the
real code in `src/java/samples/foundgine-supply-chain-advanced`, so you can build the module from
an empty Maven project and understand why each piece exists.

> Start with [`../foundgine-supply-chain/SupplyChain-Starter-Tutorial.md`](../foundgine-supply-chain/SupplyChain-Starter-Tutorial.md)
> first if you haven't — this tutorial assumes you already know why domain records are annotated
> with `@FoundgineEntity` and how the annotation processor turns them into compiled metadata.

---

## 0. Overview and setup

### What you're building

```
AI agent / MCP client
        │  JSON-RPC tools/call over HTTP
        ▼
  AdvancedMcpServer (/mcp)
        │
        ▼
  AdvancedMcpFacade
        │
        ▼
  AdvancedMutationPipeline  →  SupplyChainAuthorization  →  SupplyChainSemanticModel
        │
        ▼
  PlaceOrderService / CancelOrderService  →  PostgreSQL (PostgresSupplyChainStore)
```

### Prerequisites

- **JDK 21**, **Maven 3.9+** (same reactor as the starter)
- **Docker with Compose support** — this sample stores mutation state in real PostgreSQL
- A clone of this repository, building against the `${revision}` reactor exactly like the starter

### Create the module

```bash
mkdir -p src/java/samples/foundgine-supply-chain-advanced/src/main/java/com/foundgine/samples/supplychain/advanced
cd src/java/samples/foundgine-supply-chain-advanced
```

`pom.xml` joins the reactor the same way as the starter, with two additions: a Jackson dependency
(the MCP server encodes/decodes JSON-RPC by hand) and a `postgresql` driver dependency:

```xml
<dependencies>
  <dependency><groupId>io.github.cristianbarragan</groupId><artifactId
    >foundgine-core</artifactId><version>${revision}</version></dependency>
  <dependency><groupId>io.github.cristianbarragan</groupId><artifactId
    >foundgine-runtime</artifactId><version>${revision}</version></dependency>
  <dependency><groupId>io.github.cristianbarragan</groupId><artifactId
    >foundgine-providers</artifactId><version>${revision}</version></dependency>
  <dependency><groupId>com.fasterxml.jackson.core</groupId><artifactId
    >jackson-databind</artifactId></dependency>
  <dependency><groupId>org.postgresql</groupId><artifactId>postgresql</artifactId></dependency>
  <dependency><groupId>org.junit.jupiter</groupId><artifactId>junit-jupiter</artifactId><scope
    >test</scope></dependency>
</dependencies>
```

Add the same `maven-compiler-plugin` + `foundgine-aot-generator` annotation-processor configuration
as the starter, pointing `foundgine.generated.package` at
`com.foundgine.samples.supplychain.advanced.generated`.

Add `docker-compose.yml` next to the module:

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: foundgine_supply_chain
      POSTGRES_USER: foundgine
      POSTGRES_PASSWORD: foundgine
    ports:
      - "4429:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U foundgine -d foundgine_supply_chain"]
      interval: 2s
      timeout: 3s
      retries: 20
```

```bash
docker compose up -d postgres
```

---

## 1. Domain and semantic model

Create `domain/Domain.java` with one `record` per entity, annotated with `@FoundgineEntity`. Unlike
the starter, several fields here also carry `@FoundgineRelationship(target=..., foreignKey=...,
principalKey=..., name=...)` directly on the component that holds the foreign key, so the
relationship graph (`Company → BusinessUnit → Warehouse → InventoryLot`,
`Supplier → PurchaseOrder → PurchaseOrderLine/Shipment`) is declared alongside the field it
traverses through, instead of in a separate mapping file:

```java
@FoundgineEntity(name = "Warehouse")
public record Warehouse(
    @FoundgineRelationship(target = InventoryLot.class, foreignKey = "warehouseId", principalKey = "id", name = "inventory") int id,
    @FoundgineSemanticDimension("businessUnit")
    @FoundgineRelationship(target = BusinessUnit.class, foreignKey = "id", principalKey = "businessUnitId", name = "businessUnit") int businessUnitId,
    String name,
    @FoundgineSemanticDimension("tenant") String tenantId) {}
```

Then build the Foundgine `SemanticModel` from it in `semantics/SupplyChainSemanticModel.java`. This
sample uses the **manual builder** path (`SemanticModelBuilder`) rather than relying solely on the
AOT-generated registry, so it can declare things the annotations alone don't express yet — weighted
aliases on relationships, and two purely semantic **command entities** that don't correspond to a
domain record at all:

```java
b.entity(EntityId.create("PlaceOrderCommand"), "PlaceOrderCommand", e -> {
    e.identity(FieldId.create("PlaceOrderCommand", "OrderId"), "OrderId");
    commandField(e, "PlaceOrderCommand", "Actor", String.class);
    commandField(e, "PlaceOrderCommand", "CustomerId", int.class);
    commandField(e, "PlaceOrderCommand", "ProductId", int.class);
    commandField(e, "PlaceOrderCommand", "Quantity", int.class);
    commandField(e, "PlaceOrderCommand", "IdempotencyKey", String.class);
});
```

**Why command entities exist:** `place_order` and `cancel_order` are not CRUD on a single table —
they are business operations with their own inputs. Modeling them as semantic entities means they
flow through the _same_ mutation engine, authorization, and evidence machinery as every other
mutation, instead of being a side channel that bypasses Foundgine's boundary.

The weighted aliases mirror the C# advanced sample's "weighted alias evidence" feature exactly:

| Declaration                                   |             Weight | Scope              |
| --------------------------------------------- | -----------------: | ------------------ |
| `Supplier → Vendor` / `Seller`                |            95 / 90 | Entity alias       |
| `Supplier.Country → State`                    |                 85 | Field alias        |
| `PurchaseOrder → PO` / `POs` / `Buy` / `Buys` | 100 / 95 / 90 / 85 | Entity aliases     |
| `PurchaseOrder.ExpectedArrival → DueDate`     |                 90 | Field alias        |
| `PurchaseOrder.supplier → vendor`             |                 85 | Relationship alias |

These weights are **application-declared evidence strength**, never authority — they can make a
lexical grounding decision more confident, but they cannot create or expand a capability, and each
scope (entity/field/relationship) is checked independently.

**Checkpoint:**

```bash
cd src/java
mvn -pl samples/foundgine-supply-chain-advanced -am compile
```

---

## 2. Claims and authorization

### Claims — untrusted, narrowing-only evidence

`authorization/claims/Claims.java` validates a `Map<String, String>` of claims a caller might
attach to a request (`scope=read-only`, `warehouse=1`, `max_rows=100`, `reason=...`,
`change_ticket=CHG-1234`, `not_after=<ISO-8601>`). Three things matter here:

1. **Reserved/hostile keys are rejected outright.** `role`, `tenant`, `isAdmin`, `permissions`,
   `capabilities`, and `scopes` cannot be supplied as claims — attempting to supply them marks the
   whole claim set as a spoofing attempt (`Severity.HOSTILE`/`SUSPICIOUS`), and
   `Authorization.applyClaims` throws rather than silently drop just that key.
2. **Every validator is a narrowing function**, not a widening one: `scope=read-only` can only make
   an identity more restricted, never less. There is no claim that grants additional warehouses,
   roles, or write access.
3. **Evidence has an expiry.** If `not_after` is present and expired (or exceeds the maximum
   validity horizon), the associated evidence claims (`reason`, `change_ticket`) are stripped too —
   stale evidence is treated as no evidence.

### Authorization policy

`authorization/SupplyChainAuthorization.java` wires role-based rules into Foundgine's
`ConfiguredSemanticAuthorizationPolicy`:

```java
var config = new SemanticAuthorizationConfiguration()
    .addEntityRule((ctx, id, op) -> op == AuthorizationOperation.READ
        ? canReadEntity(id, role) : canWriteEntity(id, role, ctx.safeClaims()))
    .addFieldRule((ctx, entity, field, op) -> op == AuthorizationOperation.READ
        ? canReadField(entity, field, role) : canWriteField(entity, field, role, ctx.safeClaims()))
    .addRelationshipRule(...)
    .addPredicateRule(SupplyChainAuthorization::predicate)
    .addOperationRule((ctx, entity, op, name) -> namedOperation(ctx, op, name, role));
```

Concrete rules worth noticing:

- `CUSTOMER` cannot read `Supplier`, `SupplierCertification`, `Warehouse`, or `InventoryLot` at all
  — those are internal supply-side entities.
- `Supplier.RiskScore` is restricted to `ANALYST`/`SUPPLY_CHAIN_MANAGER` even for roles that can
  otherwise read the `Supplier` entity — the same **field-level** authorization-beneath-capability
  pattern the C# advanced sample demonstrates with `Supplier.NegotiatedCost`.
- `InventoryLot.Quarantined` is restricted to `WAREHOUSE_OPERATOR`/`SUPPLY_CHAIN_MANAGER`.
- Only `SUPPLY_CHAIN_MANAGER` may write through the `inventory` relationship, and only when the
  caller's claims haven't reduced them to read-only.

This is the same order of operations the starter's simpler `Authorization.Context` predicates
gesture at, now enforced _by the planner itself_ through a configured policy object rather than by
application code remembering to call a check.

---

## 3. High-assurance mutation: place and cancel an order

`mutation/AdvancedMutationPipeline.java` is the full path: intent → semantic plan → authorization →
ExecutionIR → provider:

```java
this.engine = new FoundgineMutationEngine(schema, policy, provider, model, null, null, null, null);

public CompletionStage<MutationExecutionResult> placeOrder(String actor, int customerId, int productId,
                                                            int quantity, String idempotencyKey) {
    var op = new SemanticMutationOperation(
        PLACE_ORDER, SemanticMutationKind.CREATE,
        List.of(value(PLACE_ORDER, "Actor", actor), value(PLACE_ORDER, "CustomerId", customerId),
                value(PLACE_ORDER, "ProductId", productId), value(PLACE_ORDER, "Quantity", quantity),
                value(PLACE_ORDER, "IdempotencyKey", idempotencyKey)),
        null, List.of(), List.of(field(PLACE_ORDER, "OrderId")), List.of(), List.of());
    return engine.executeAsync(new SemanticMutationRequest(new SemanticMutationOperationGraph(List.of(op))),
        ExecutionContext.EMPTY, CancellationToken.NONE);
}
```

The engine's `provider` argument is a `DomainMutationProvider` — an application adapter implementing
`IMutationBatchExecutionProvider` and `IMutationSecurityConformanceEvaluator` — that receives the
already-authorized, already-planned `ExecutionMutationIR` and dispatches by command entity name to
either `PlaceOrderService` or `CancelOrderService`:

```java
if ("PlaceOrderCommand".equals(name)) {
    var service = new PlaceOrderService(data);
    var r = service.placeOrder(actor, auth, customerId,
        List.of(new PlaceOrderService.OrderLine(productId, quantity)), idempotencyKey);
    results.add(new MutationResult(1, Map.of(field(PLACE_ORDER, "OrderId"), r.orderId())));
}
```

`PlaceOrderService.placeOrder` then enforces the same invariants as the C# `PlaceOrder`, in this
order:

1. Reject empty line lists and non-positive quantities.
2. Require an idempotency key.
3. Check the caller's role/read-only flag can place orders at all.
4. Compute a **request fingerprint** (SHA-256 of actor + customer + sorted lines) and, under a
   per-key lock, check it against any prior use of the same idempotency key — a replay with the
   _same_ request returns the original result (`replay = true`); a replay with a _different_
   request throws.
5. Verify the customer belongs to the caller's tenant, and (for `CUSTOMER` role) that the actor owns
   that customer.
6. Resolve each product against inventory lots the caller's authorization context can see
   (`auth.allowedWarehouses()`), picking a lot with enough unreserved, unquarantined stock.
7. Compute the total server-side from `Product.unitPrice` — never trust a caller-supplied price.
8. Apply the order, order items, allocations, and inventory decrement atomically, rolling every
   collection back to its pre-mutation snapshot if any step throws.
9. Record the idempotency key against the resulting order id.

`CancelOrderService.cancelOrder` mirrors this shape in reverse: verify the order and actor, restore
the allocated inventory quantity to its original lot, and record a **separate**
`CancellationIdempotencyRecord` keyed by its own idempotency key, so a `place_order` replay key can
never be reused to short-circuit a `cancel_order`, and vice versa.

Run the smoke test to see both operations — and both of their idempotent replays — end to end:

```bash
mvn -pl samples/foundgine-supply-chain-advanced -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.advanced.Main
```

You should see matching `planFingerprint`/`resultFingerprint` pairs for the original call and its
replay, proving the replay didn't re-execute the mutation.

---

## 4. PostgreSQL: the same guarantees against a real database

`postgres/PostgresSupplyChainStore.java` implements the identical `place_order`/`cancel_order`
contract against real PostgreSQL instead of the in-memory fixture, using:

- `pg_advisory_xact_lock(hashtext(?))` on the idempotency key, so two concurrent requests carrying
  the same key serialize instead of racing each other into two different orders.
- A `request_fingerprint` column on `supply_chain_idempotency`, checked with `FOR SHARE` before any
  write — the SQL-level version of the in-memory service's fingerprint check.
- The same schema shape as the C# sample's PostgreSQL tables, adapted to this sample's entities —
  see `src/main/resources/database/schema.sql` (`customers`, `products`, `warehouses`,
  `inventory_lots`, `orders`, `order_items`, `order_allocations`, `supply_chain_idempotency`,
  `supply_chain_cancellation_idempotency`) and `seed.sql` for the deterministic fixture data,
  matching `SupplyChainData.seed()` row for row.

`postgres/PostgresAdvancedMutationProvider.java` is the `IMutationBatchExecutionProvider`
implementation that plugs `PostgresSupplyChainStore` into `AdvancedMutationPipeline` in place of the
in-memory `DomainMutationProvider` — swap it in when you construct the pipeline against a real
`Connection` and every guarantee above holds against PostgreSQL instead of a `List` in memory.

Apply the schema and seed once PostgreSQL is up:

```bash
docker exec -i $(docker compose ps -q postgres) \
  psql -U foundgine -d foundgine_supply_chain < src/main/resources/database/schema.sql
docker exec -i $(docker compose ps -q postgres) \
  psql -U foundgine -d foundgine_supply_chain < src/main/resources/database/seed.sql
```

---

## 5. MCP transport and adversarial security testing

### The MCP server

`mcp/AdvancedMcpServer.java` hosts `AdvancedMcpFacade` behind a minimal JSON-RPC-over-HTTP `/mcp`
endpoint, using only the JDK's built-in `com.sun.net.httpserver.HttpServer` — no MCP SDK dependency,
matching the C# sample's `MCP.Foundgine` project but with a hand-rolled transport instead of
`ModelContextProtocol.AspNetCore`. It exposes exactly two tools:

- `foundgine_query` — `{"tool": "capabilities"}` for capability discovery.
- `foundgine_mutation` — `{"tool": "place_order", "actor": ..., "customerId": ..., "productId": ...,
"quantity": ..., "idempotencyKey": ...}` or the `cancel_order` equivalent.

```bash
mvn -pl samples/foundgine-supply-chain-advanced -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.advanced.mcp.AdvancedMcpServer
```

`mcp/AdvancedMcpClientDemo.java` is a matching raw-HTTP client (`java.net.http.HttpClient`,
protocol version `2025-06-18`) that calls the server the same way any MCP-compatible client would —
run it against a running server to see `place_order` and `cancel_order` executed over the wire.

### Adversarial invariants

`scenarios/Scenarios.java` and `scenarios/ScenariosAdversarialInvariants.java` are the Java
equivalent of the C# advanced sample's security test suite (`RecursiveSupplierRiskTests`, graph
security boundary, sensitive-field authorization) — but run against the **live** semantic model and
authorization context built in steps 1–2, not a disconnected fixture:

- **Recursive supplier risk** — the same cycle-safe BOM traversal as the starter's `Scenarios`, but
  now evaluated through `Authorization.Context` so a caller without a warehouse grant simply doesn't
  see risk exposure for products only stocked there.
- **Fulfillment risk** — computes shortage (`demand - available - inbound`) per product across the
  caller's authorized warehouses, exercising the same relationship traversal
  (`Product → InventoryLot → Warehouse`) the authorization rules in step 2 gate.
- **Tenant isolation** — `Authorization.canReadWarehouse` denies warehouse 3 (`tenant-b`) to a
  `tenant-a` context, the same invariant asserted in the starter, now checked as part of a scenario
  that also touches the semantic model and claims machinery.

Run the smoke test one more time and read its output against this section — `Main` prints the
detected cycle count, fulfillment row count, and the plan/result fingerprints for both mutations, so
you can see every piece from steps 1–5 exercised together in one process.

---

## Where this leaves you

You now have the full **MCP → semantic model → authorization → mutation engine → PostgreSQL** path
working in Java, with claims-based scope reduction, high-assurance `place_order`/`cancel_order`,
and the adversarial invariants that prove the boundary holds under malformed and untrusted input.

Not yet ported from the C# advanced sample: the ambiguity-resolution capability
(`find_top_supplier_overdue_orders`), the PostgreSQL retrieval strategies (`pg_trgm` fuzzy,
full-text, optional `pg_search`/BM25, optional Apache AGE graph similarity), and the GraphQL/Hot
Chocolate surface. These remain tracked against the C# reference in
[`../../../csharp/samples/Foundgine.SupplyChain.Advanced/docs/`](../../../csharp/samples/Foundgine.SupplyChain.Advanced/docs/03-Ambiguity-And-Grounding.md)
as the next parity work for this module.
