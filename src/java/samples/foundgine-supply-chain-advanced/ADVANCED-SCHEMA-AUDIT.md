# Advanced Supply Chain — PostgreSQL schema audit

## Purpose and current decision

The Advanced sample's `V1__supply_chain_advanced.sql` remains the reviewed physical baseline until
JDK 27 Maven tests and PostgreSQL 16 comparison pass. Checkpoint 13 adds
`AdvancedPhysicalSchemaModel`, a physical-schema projection that registers exactly the eleven V1
persistence configurations from the same `PersistenceConfigurations` used by
`SupplyChainFoundgineModel`. It prevents the seventeen semantic/planning persistence placeholders
from becoming accidental PostgreSQL tables. The projection reuses the fluent declarations; it does
not duplicate table or column definitions.

Do **not** replace V1 or generate/apply a production migration until the supported build and
PostgreSQL 16 parity checks pass. The application semantic model remains broader than the physical
V1 schema by design.

## Physical tables in V1

| Physical table | Primary key | Important shape / constraints | Used by |
|---|---|---|---|
| `customers` | `customer_id` | `customer_name varchar(120)`, `tenant_id varchar(80)`; both required | `PostgresSupplyChainStore` order ownership and idempotency |
| `products` | `product_id` | SKU unique; `product_name varchar(120)`; `category varchar(80)`; `safety_stock numeric(18,2)`; `unit_price numeric(18,2)` with `CHECK (unit_price >= 0)` | JPA `ProductEntity`, semantic `Product`, order SQL |
| `warehouses` | `warehouse_id` | `warehouse_name varchar(120)`, `business_unit_id`, `tenant_id`; required | JPA `WarehouseEntity`, semantic `Warehouse` |
| `inventory_lots` | `lot_id` | FKs to warehouses/products; `on_hand`, `reserved`, `quarantined` numeric(18,2), nonnegative checks; `reserved` and `quarantined` default 0; received date; `(product_id, warehouse_id)` index | JPA `InventoryLotEntity`, semantic `InventoryLot`, allocation SQL |
| `orders` | identity `order_id` | FK customer; status CHECK over Pending/Cancelled/Processing/Shipped/Delivered; total numeric(18,2); placed date | `PostgresSupplyChainStore` |
| `order_items` | identity `order_item_id` | FKs to orders/products; cascade delete from order; positive quantity CHECK; unit price numeric(18,2) | `PostgresSupplyChainStore` |
| `order_allocations` | `order_item_id` | FK order item with cascade delete; FK lot; positive quantity CHECK | `PostgresSupplyChainStore` |
| `supply_chain_idempotency` | `idempotency_key` | actor/customer/order FKs; 64-char request fingerprint; timestamp default | `PostgresSupplyChainStore` |
| `supply_chain_cancellation_idempotency` | `idempotency_key` | actor/order FK; request fingerprint; restored quantity; cancellation date | `PostgresSupplyChainStore` |
| `suppliers` | identity `supplier_id` | unique supplier name; state; total order value numeric(14,2) default 0; negotiated cost numeric(14,2); `pg_trgm` GIN index | `TopSupplierOverdueOrdersService` |
| `purchase_orders` | identity `purchase_order_id` | FK supplier; expected/received dates; status default Open with CHECK; supplier index | `TopSupplierOverdueOrdersService` |

The baseline also runs `CREATE EXTENSION IF NOT EXISTS pg_trgm`. This extension and its GIN operator
class are PostgreSQL-specific prerequisites for the ambiguity-resolution fuzzy-match query. The
table inventory above lists 11 tables (the source migration is the authority if this document and
the SQL ever differ).

## Model and schema drift

`SupplyChainFoundgineModel` retains these 17 original semantic persistence configurations:

| Fluent entity | Current configured table | V1 physical counterpart | Assessment |
|---|---|---|---|
| `Company` | `Company` | — | No V1 table |
| `BusinessUnit` | `BusinessUnit` | — | No V1 table |
| `Warehouse` | `warehouses` | `warehouses` | Partial match; column names/types and FK policy need explicit mapping |
| `Supplier` | `Supplier` | `suppliers` | Name mismatch; semantic model includes tenant/risk fields absent from V1 supplier table |
| `SupplierSite` | `SupplierSite` | — | No V1 table |
| `SupplierCertification` | `SupplierCertification` | — | No V1 table |
| `Product` | `products` | `products` | Partial match; unique SKU, check constraint and exact precision/defaults need reconciliation |
| `ProductComponent` | `ProductComponent` | — | No V1 table |
| `PurchaseOrder` | `PurchaseOrder` | `purchase_orders` | Not equivalent: model has warehouse/order date/enum shape; SQL table has supplier/expected/received/status |
| `PurchaseOrderLine` | `PurchaseOrderLine` | — | No V1 table |
| `Shipment` | `Shipment` | — | No V1 table |
| `InventoryLot` | `inventory_lots` | `inventory_lots` | Partial match; FK/check/default semantics are not represented in current generator |
| `CustomerOrder` | `CustomerOrder` | `orders` | Not equivalent: model uses business unit; SQL uses customer and total amount |
| `CustomerOrderLine` | `CustomerOrderLine` | `order_items` | Not equivalent: quantity type and field shape differ |
| `InventoryMovement` | `InventoryMovement` | — | No V1 table |
| `ProductionOutput` | `ProductionOutput` | — | No V1 table |
| `ComplianceIncident` | `ComplianceIncident` | — | No V1 table |

The physical transactional tables `customers`, `orders`, `order_items`, `order_allocations`, and both
idempotency tables now have dedicated persistence row configurations (Checkpoint 10). These rows are
kept separate from semantic/planning records where the shapes differ. Several of the original fluent
entities remain semantic/planning concepts rather than tables in this database.

The JPA ORM path currently maps only `WarehouseEntity`, `InventoryLotEntity` and `ProductEntity`.
`AdvancedJpaOrmTest` and the JPA entity classes are therefore not evidence that the full V1 schema
is represented by the fluent model.

## Safe reconciliation sequence

1. **Freeze the V1 contract.** Keep `V1__supply_chain_advanced.sql` unchanged while documenting all
   SQL read/write paths, FKs, CHECKs, defaults, identity behavior, extensions and indexes.
2. **Separate semantic entities from persisted entities.** Decide which of the 17 fluent entities
   are truly persisted in this PostgreSQL database, and model helper/idempotency tables explicitly
   where they are part of the durable contract.
3. **Map physical names and columns.** For every persisted entity, align `toTable`, property column
   names, SQL types/precision, nullability, keys and indexes with V1. Do not infer table equivalence
   from similar names (`PurchaseOrder` is not `purchase_orders` in the current schema).
4. **Represent unsupported DDL deliberately.** The current generator supports tables, columns,
   primary keys and indexes; it does not yet generate foreign keys, CHECK constraints, defaults,
   identity semantics or extension declarations. Preserve these in handwritten, reviewed SQL or
   extend the schema model/differ/generator and tests before attempting full model ownership.
5. **Add drift tests.** Build the Advanced `MetadataRegistry`, derive its schema, and assert the
   expected persisted table/column/index contract. Add explicit tests for every unsupported DDL
   fragment that remains hand-maintained so model changes cannot silently diverge.
6. **Adopt generated migrations only after parity.** The first generated Advanced migration should
   be a controlled baseline after comparing normalized table/column/index definitions against
   PostgreSQL 16 and the existing V1. Do not create an automatic V2 that duplicates or renames the
   existing V1 objects.

## Checkpoint 9 — first persistence reconciliation increment

The fluent persistence configurations for the three entities already backed by the Advanced JPA
path now describe their physical V1 columns rather than relying on Java member-name conventions:

- `Product` → `products`: `product_id`, `sku`, `product_name`, `category`, `safety_stock`,
  `unit_price`; exact `varchar`/`numeric(18,2)` types and the unique SKU index
  (`products_sku_key`) are declared.
- `Warehouse` → `warehouses`: `warehouse_id`, `warehouse_name`, `business_unit_id`, `tenant_id`;
  exact V1 lengths and required columns are declared. The previous fluent business-unit index was
  removed because V1 does not create that index.
- `InventoryLot` → `inventory_lots`: all seven V1 columns, exact numeric precision/date types, and
  the named `(product_id, warehouse_id)` index are declared.

`AdvancedSchemaModelParityTest` derives the schema through `SchemaBuilder` and locks these names,
types, required-column semantics, keys and indexes to the V1 contract. It also checks the transactional tables and the supplier/overdue-purchase-order tables described below.

The Advanced sample now has a test-scope dependency on `foundgine-migrations` for this schema
contract test. This does **not** enable the migration CLI for the full Advanced model: the remaining
model/storage mismatches and unsupported DDL are still unresolved.

- `V1__supply_chain_advanced.sql` and Flyway history remain unchanged.
- No new migration or snapshot was generated.
- Foreign keys, CHECK constraints, defaults, identity behavior, and the `pg_trgm` GIN/operator-class index are still outside the fluent model's generated-schema contract.
- Maven verification remains pending because this environment has JDK 21 and no Maven executable;
  the project targets JDK 27.

## Next reconciliation boundary

The next increment should extend the generator's relational metadata for identity generation, foreign
keys and CHECK constraints, and add explicit support for PostgreSQL extensions and index access methods
/operator classes. Until then, preserve the reviewed V1 SQL for those clauses.

## Checkpoint 10 — transactional persistence-row reconciliation

The six transactional V1 tables that were previously represented only by direct SQL/domain records
now have explicit physical persistence row types in `AdvancedPersistenceRows`. They are separate
from semantic records such as `CustomerOrder`, `PurchaseOrder` and `Supplier`; no semantic entity
was repurposed to fit an unrelated SQL table.

| V1 table | Physical row type | Modeled contract |
|---|---|---|
| `customers` | `CustomerRow` | `customer_id` PK; `customer_name varchar(120)`; `tenant_id varchar(80)` |
| `orders` | `OrderRow` | `order_id` PK; customer/status/total/date columns; `(customer_id,status)` index |
| `order_items` | `OrderItemRow` | `order_item_id` PK; order/product/quantity/unit-price columns |
| `order_allocations` | `OrderAllocationRow` | `order_item_id` PK; lot and quantity columns |
| `supply_chain_idempotency` | `IdempotencyRow` | key PK; actor/customer/order/fingerprint/timestamp columns |
| `supply_chain_cancellation_idempotency` | `CancellationIdempotencyRow` | key PK; actor/order/fingerprint/restored quantity/cancelled date columns |

`SupplyChainFoundgineModel` registers these six persistence configurations. The expanded
`AdvancedSchemaModelParityTest` derives their metadata with `SchemaBuilder` and checks exact V1
column names, normalized SQL types, NOT NULL behavior, primary keys and the orders composite index.
The original domain records and transactional service constructor call sites are unchanged.

This is metadata parity for the table/column/key/index subset supported by the current generator,
not complete DDL parity. In particular, the current generator still cannot express or emit:

- `GENERATED ALWAYS AS IDENTITY` on `orders.order_id` and `order_items.order_item_id`;
- foreign keys and `ON DELETE CASCADE` actions;
- CHECK constraints (positive quantities and the allowed order status set);
- `CURRENT_TIMESTAMP` default on `supply_chain_idempotency.created_at`.

Those clauses remain owned by the unchanged V1 migration. No generated migration or snapshot was
created, and no Flyway SQL was modified. The two ambiguity-resolution tables (`suppliers` and
`purchase_orders`) remain for the next reconciliation increment.

Build verification was not run in this environment: the project requires JDK 27, while the
available runtime is JDK 21, and Maven is not installed. The test source and POM were updated but
must be executed in the supported build environment.


## Checkpoint 11 — supplier and purchase-order reconciliation

The physical ambiguity-resolution tables now have dedicated row types and persistence configurations:

| V1 table | Physical row type | Modeled contract |
|---|---|---|
| `suppliers` | `SupplierRow` | supplier ID key; name `varchar(100)` and unique index; nullable state `varchar(2)`; total order value `numeric(14,2)`; nullable negotiated cost `numeric(14,2)` |
| `purchase_orders` | `PurchaseOrderRow` | purchase-order ID key; supplier ID; expected date; nullable received date; status `varchar(20)`; supplier index |

These row types are intentionally distinct from the semantic `Supplier` and `PurchaseOrder` types. The
parity tests check exact columns, SQL types, nullability, primary keys and the unique/supplier indexes.
The `pg_trgm` extension and `ix_suppliers_name_trgm` GIN index are not represented by the current
fluent index API (which does not express access methods or operator classes). Likewise, the model does
not yet encode identity generation, foreign keys, CHECK constraints, or defaults. The original V1 and
repeatable demo-data SQL remain unchanged and authoritative for those behaviors.

No generated migration or snapshot was created. The Advanced sample is not yet safe to switch to
model-generated migrations. Maven/JUnit verification remains pending in an environment with JDK 27
and Maven; this checkpoint was statically inspected only.


## Checkpoint 13 — fluent physical-schema integration

Core metadata and the fluent persistence API now carry SQL types/precision, defaults, identity
generation, named foreign keys and referential actions, CHECK constraints, index access methods and
operator classes, and PostgreSQL extension requirements. `FoundgineModelCompiler` preserves these
properties for both mapped entities and persistence-only row types. `SchemaBuilder.fromMetadata`
transfers them into the migration schema model.

The Advanced physical-schema projection is `AdvancedPhysicalSchemaModel`. It registers only the
three existing semantic persistence configurations that correspond to V1 tables (`products`,
`warehouses`, `inventory_lots`) and the eight dedicated V1 physical-row configurations. All eleven
registrations point to the same configuration classes used by `SupplyChainFoundgineModel`, so the
physical DDL remains authored once. `AdvancedSchemaModelParityTest` now derives its schema from this
projection and asserts that the table set is exactly the eleven V1 tables, in addition to checking
column, key, index, identity, default, FK, CHECK, extension, SQL-fragment and snapshot behavior.

The full semantic registry intentionally still contains additional semantic/planning persistence
metadata. It is not itself a physical-schema projection and must not be passed directly to the
migration baseline generator for Advanced.

Verification remains pending: this environment has JDK 21, no Maven executable, and no PostgreSQL
16 server/client. The archive has not been compiled or integration-tested here. V1 remains unchanged.


## Checkpoint 13 follow-up — complete V1 declaration coverage

A source review against `V1__supply_chain_advanced.sql` found that the fluent `InventoryLotStorage`
configuration had the two non-null V1 defaults missing even though it modeled the corresponding
CHECK constraints. The `reserved` and `quarantined` properties now both declare `DEFAULT 0`.

`AdvancedSchemaModelParityTest` now explicitly covers all V1 constraint categories and asserts the
expected aggregate counts: 11 foreign keys, 8 CHECK constraints, 6 explicit indexes (including
unique indexes), 5 column defaults, and 4 identity columns. It also checks the product price CHECK,
all inventory-lot foreign keys/checks, both order-item foreign keys, both allocation foreign keys,
both idempotency relationships, cancellation idempotency, and the purchase-order supplier FK/status
CHECK/default/index. The test class documentation now describes the full eleven-table contract rather
than the earlier three-entity scope.

This is a source-level parity improvement only. JUnit execution and PostgreSQL 16 validation remain
pending in an environment with JDK 27, Maven, and a PostgreSQL 16 server. The handwritten V1 migration
has not been changed.

## PostgreSQL 16 catalog integration test

`AdvancedPostgres16SchemaIntegrationTest` now creates two isolated schemas in a disposable
PostgreSQL 16 database. It applies the checked-in handwritten V1 migration to the reference
schema and SQL generated from `AdvancedPhysicalSchemaModel` to the candidate schema, then
compares PostgreSQL catalog projections for columns (types, nullability, defaults and identity
modes), primary/foreign/check constraint definitions, and index definitions (including unique
and GIN/operator-class indexes). Constraint names are intentionally excluded from the semantic
constraint comparison because PostgreSQL-generated inline names differ from explicitly named
fluent declarations; unique constraints are compared through their backing unique indexes. The
check also verifies the eleven-table result and the `pg_trgm` GIN index.

Enable it with:

```bash
export FOUNDGINE_POSTGRES16_JDBC_URL='jdbc:postgresql://localhost:5432/foundgine_test'
export FOUNDGINE_POSTGRES16_USER='postgres'
export FOUNDGINE_POSTGRES16_PASSWORD='...'
./mvnw -pl samples/foundgine-supply-chain-advanced -am -Dtest=AdvancedPostgres16SchemaIntegrationTest -Dsurefire.failIfNoSpecifiedTests=false test
```

The test skips when `FOUNDGINE_POSTGRES16_JDBC_URL` is absent and fails fast if the
connected server is not PostgreSQL 16. Use a disposable database: the test creates and
drops a uniquely named schema, while a newly installed `pg_trgm` extension remains
installed at database scope.

## Generated Flyway migration (review-only until PostgreSQL parity is proven)

`AdvancedMigrationGenerator` is the supported deterministic entry point for producing the
Advanced V1 DDL from `AdvancedPhysicalSchemaModel`. It intentionally writes to a generated
output path and never replaces the checked-in `V1__supply_chain_advanced.sql` implicitly.

From `src/java` with JDK 27 and Maven installed:

```bash
mvn -pl samples/foundgine-supply-chain-advanced -am -DskipTests install
mvn -pl samples/foundgine-supply-chain-advanced \
  -Dexec.mainClass=com.foundgine.samples.supplychain.advanced.migrations.AdvancedMigrationGenerator \
  -Dexec.args=target/generated-migrations/V1__supply_chain_advanced.generated.sql \
  org.codehaus.mojo:exec-maven-plugin:3.5.0:java
```

The generated file must be reviewed and applied to a disposable PostgreSQL 16 database before
any adoption decision. Compare its catalog result with a clean application of the existing V1
migration, including column types/nullability/defaults, identity modes, primary/foreign keys,
CHECK constraints, indexes/operator classes, and extensions. A matching object count alone is
not sufficient evidence of parity. Keep the generated file outside Flyway's configured migration
location during this comparison to avoid accidental double application.


### Checkpoint 13 follow-up — handwritten/generated catalog parity harness

The PostgreSQL 16 integration test now applies both schema sources side-by-side: the checked-in
`V1__supply_chain_advanced.sql` and the SQL generated from `AdvancedPhysicalSchemaModel`. It
compares catalog-level column metadata, constraint semantics, and index definitions rather than
only asserting object counts. The `pg_trgm` extension is installed once in `public` so extension
placement does not contaminate the schema comparison. Equivalent constraint definitions are
compared independently of PostgreSQL-generated constraint names; unique constraints are compared
through their unique indexes because the fluent model exposes the existing unique-index API.

This is a stronger test harness, not a passing parity result. The test has not been executed: this
environment has JDK 21, no Maven, and no PostgreSQL 16 server. The V1 migration remains unchanged.
