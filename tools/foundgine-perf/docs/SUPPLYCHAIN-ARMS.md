# Supply-chain ORM pairs with Keycloak and real migrations

Four arms, two like-for-like pairs. Compare **within** a runtime only (JIT, GC and ORM differ across runtimes). All four arms now use the same Redis 7.4 service and a PostgreSQL durable revocation epoch anchor; C# and Java use isolated Redis key namespaces (`benchmark:csharp:` and `benchmark:java:`).

| Pair | Baseline ("normal" implementation) | Foundgine arm |
|---|---|---|
| C# | Hot Chocolate + plain EF Core, migrations from a plain `dotnet ef migrations add Baseline` | Foundgine GraphQL + Foundgine EF provider; schema from the **Foundgine EF migration set** (`dotnet ef` over the Foundgine-derived model, applied by `DatabaseMigrator`) |
| Java | Spring GraphQL + plain Hibernate/JPA, hand-written Flyway `V1__baseline.sql` | Foundgine GraphQL + Foundgine JPA provider; schema from **foundgine-migrations** (`V<ts>__InitialCreate.sql` generated from the fluent model, applied by Flyway) |

Domains come from the existing samples: `src/csharp/samples/Foundgine.SupplyChain` (orders, lines, shipments, products...) and
`src/java/samples/foundgine-supply-chain` (Supplier, Warehouse, Product; tenant-scoped). They differ, so each pair has its own scenarios.

## Identity: the CRM/ERP approach, one realm for all arms
`keycloak/gen_realm.py` -> `supplychain-realm.json`. Same claim shape the e2e CRM (C#) and ERP (Java/Spring) read:
realm roles in `realm_access.roles`, `tenant_id`, one audience (`supplychain-api`, like `crm-api`/`erp-api`), plus `customer_id` for
customer users. Every arm validates issuer + audience + signature + lifetime; a token with no `tenant_id` is denied (ERP `requireTenant`). Each arm also checks shared Redis revocation state against its PostgreSQL high-water anchor and fails closed on Redis rollback or anchor unavailability. The Hot Chocolate baseline uses direct StackExchange.Redis/Npgsql code rather than Foundgine provider packages, so it receives the same infrastructure without using the framework being benchmarked.

| Where to copy the pattern from | C# arms | Java arms |
|---|---|---|
| Bearer validation | `Foundgine.Extensions.Keycloak` `AddFoundgineKeycloak` (as `crm/.../IdentityExtensions.cs`) in the Foundgine arm; plain `AddJwtBearer` in the baseline | Spring `oauth2ResourceServer` + `SecurityConfig` converter (as `erp/.../SecurityConfig.java`) in both arms |
| Caller from token | `RoleGrants` config (as CRM `appsettings.json`) | `ErpCaller`-style record |
| Foundgine authorization | role grants + row predicate through Foundgine's semantic policy | `FoundgineOrm.Authorization<Caller>` (as `ErpAuthorization.java`): entity/field/relationship rules + tenant predicate |
| Baseline authorization | hand-written filters in resolvers | hand-written filters in services |

Users: alice/bob (`sc-customer`, customer 1/2, tenant-a), carol (`sc-operator`), dave (`sc-analyst`), erin (`sc-analyst`, tenant-b),
admin (`sc-manager`), mallory (no role), notenant (manager, no `tenant_id`). Password `dev-password`.

### C# rules (vectors: `scenarios/vectors-supplychain-csharp.json`)
| Role | Read | Write |
|---|---|---|
| sc-customer | own `SalesOrder` rows (`customerId` = token `customer_id`) with nested lines/shipments, `CatalogProduct`; NOT root `Shipment`/`Supplier` | create `SalesOrder` for own customer only |
| sc-operator | all orders, shipments, products, inventory | update `Shipment` |
| sc-analyst | products, suppliers, inventory; NOT orders | none |
| sc-manager | everything | everything |

### Java rules (vectors: `scenarios/vectors-supplychain-java.json`)
| Role | Read | Write |
|---|---|---|
| sc-customer | `Product` | none |
| sc-operator | `Supplier` (not `riskScore`), `Warehouse`, `Product`, tenant rows only | update `Warehouse`, own tenant only |
| sc-analyst | `Supplier` incl. `riskScore`, `Product`; tenant rows only; NOT `Warehouse` | none |
| sc-manager | all three, tenant rows only | all, own tenant only |

Selecting a field you may not read, or reading an entity you may not read, is a **denied request** (error), not a masked null. A
write that matches no row in the caller's tenant also counts as denied (all root fields null).

## Migrations: what each arm must prove
Each arm has its own database (`compose/csharp/supplychain-init.sql` and `compose/java/supplychain-init.sql`). Schema comes only from that arm's migration tool; the harness applies
identical benchmark data afterwards (`seed/reset-*.sql`). `fpbench schemaparity` reads the live databases (`scenarios/schema_dump.sql`) and fails if
* an arm has **no history table** (EnsureCreated / Hibernate `ddl-auto` / hand SQL),
* the applied migration ids do not match the arm's tool (`expect_history`) or match the other tool (`forbid_history`),
* the two arms of a runtime differ in tables, columns, types, nullability, keys, FKs or indexes (constraint/index *names* are ignored).

**Required before the C# Foundgine arm counts:** the checked-in `Migrations/20261115000000_InitialSupplyChain*` was generated by
`tools/gen_migration.py` without an SDK (timestamp `...000000`, pending-model-changes warning suppressed). Regenerate it with
`dotnet ef migrations add InitialSupplyChain --project <foundgine arm>` (uses `SupplyChainDesignTimeFactory`), then
`dotnet ef migrations has-pending-model-changes` must be clean and the `PendingModelChangesWarning` suppression removed. The Java
migration (`V20261114161434__InitialCreate.sql`) is real foundgine-migrations output; re-run `./migrations.sh migrations has-pending-model-changes`.

## Gate order (nothing is a result until all pass)
```
docker compose -f compose/keycloak.yml -f compose/supplychain.yml -f compose/supplychain.perf.yml up -d --build
python3 -m fpbench schemaparity configs/supplychain-csharp.json --out out/sc-cs      # provenance + same schema
python3 -m fpbench armauthz     configs/supplychain-csharp.json --vectors scenarios/vectors-supplychain-csharp.json --out out/sc-cs
python3 -m fpbench run          configs/supplychain-csharp.json --out out/sc-cs       # response-equivalence probe runs first
# same for java. armauthz mutates data (order create): re-seed afterwards (the runner's reset_cmd does it per cell).
```

## Status
Source-level implementation now exists for all four arms. Redis 7.4 is shared across the compose stack with AOF and `appendfsync=always`; Redis resource usage is included in every arm's resource sample. C# uses `benchmark:csharp:` and Java uses `benchmark:java:` namespaces. Each arm has its own PostgreSQL durable revocation epoch table in its own benchmark database. The baseline implementations use direct Redis/JDBC or Redis/Npgsql APIs; the Foundgine implementations use the corresponding provider packages.

**Verified in this environment:** compose YAML and project XML parse; the supply-chain wiring suite passes (8 tests); migration-history expectations now distinguish the C# baseline `*_Baseline` migration from the Foundgine `*_Initial` migration.

**Not verified here:** .NET compilation, Maven compilation (the project requires Java 27; this environment has Java 21 and no Maven executable), live Redis/PostgreSQL integration, Keycloak import/token validation, actual migration application, HTTP response equivalence, and performance runs. Do not treat the benchmark as accepted or its results as comparable until those runtime gates pass.

## Run and verify

```bash
docker compose -f compose/keycloak.yml -f compose/supplychain.yml -f compose/supplychain.perf.yml up -d --build
python3 -m fpbench schemaparity configs/supplychain-csharp.json --out out/sc-cs
python3 -m fpbench armauthz configs/supplychain-csharp.json --vectors scenarios/vectors-supplychain-csharp.json --out out/sc-cs
python3 -m fpbench run configs/supplychain-csharp.json --out out/sc-cs
python3 -m fpbench schemaparity configs/supplychain-java.json --out out/sc-java
python3 -m fpbench armauthz configs/supplychain-java.json --vectors scenarios/vectors-supplychain-java.json --out out/sc-java
python3 -m fpbench run configs/supplychain-java.json --out out/sc-java
```

Before performance runs, verify `/health/ready` returns 200 for all four arms. It now includes a Redis write/read/delete probe and a PostgreSQL primary-backed high-water synchronization check. Redis is intentionally shared by the arms but namespaces are isolated by runtime; restart the app between measurement cells, not Redis. Re-seed after `armauthz`, which includes mutating authorization probes.

## Migration provenance scripts

`tools/foundgine-perf/csharp/` and `tools/foundgine-perf/java/` hold the same three scripts per runtime:
`generate-baseline-migration.sh`, `generate-foundgine-migration.sh` and `verify-migration-provenance.sh`.
In `java/`, the baseline script only checks that the hand-authored `V1__baseline.sql` is in place, the Foundgine
script runs the `foundgine-migrations` CLI through `src/java/samples/foundgine-supply-chain/migrations.sh`, and the
provenance script checks source artifacts only (no local migration copy in the Foundgine arm, Hibernate never creates the schema).
`tests/test_java_migration_scripts.py` covers them without Maven or a database.
