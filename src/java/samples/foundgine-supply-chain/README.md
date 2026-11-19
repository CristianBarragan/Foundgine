# Foundgine Supply Chain (Java) — Starter Sample

The Java counterpart to [`Foundgine.SupplyChain`](../../../csharp/samples/Foundgine.SupplyChain/README.md), on the same
fluent pattern as the e2e CRM reference. Everything is configured through the fluent API; there are no
`@FoundgineEntity` annotations.

**Caller → application → fluent semantic model → authorization → Foundgine planning/execution → JPA → PostgreSQL**

> **Verification note.** The migration generator and starter migration tests have been exercised
> against PostgreSQL 16. The Flyway Java integration and Hibernate/JPA path still need a local
> `mvn verify`; they were not verified in the environment used for this checkpoint.

## Layout

| Path | Role |
| --- | --- |
| `domain/Domain.java` | Annotation-free records: `Supplier`, `Warehouse`, `Product`. |
| `model/SupplyChainFoundgineModel.java` | The application registration point (`FoundgineModel`) for semantics, persistence and entity maps. |
| `model/SimplePhysicalSchemaModel.java` | Physical-schema projection used by migration generation; registers only the three persisted tables. |
| `model/SemanticConfigurations.java` | `FoundgineModelConfiguration<T>`: names, aliases (`Vendor`, `Seller`), dimensions. |
| `model/PersistenceConfigurations.java` | `FoundginePersistenceConfiguration<T>`: authoritative tables, columns, keys and indexes. |
| `model/EntityMaps.java` | `FoundgineEntityMap<T, T>`: semantic member → storage member, via method references. |
| `jpa/*Entity.java` | JPA persistence classes (`@FoundgineOrmEntity` generates the ORM maps and direct-access models). |
| `jpa/SupplyChainOrm.java` | `FoundgineOrm` configuration: fluent metadata + generated maps/models + mandatory authorization. |
| `jpa/SupplyChainPolicy.java` | Tenant predicate, `riskScore` field rule, customer entity limits; writes and relationships denied. |
| `jpa/SupplyChainDatabase.java` | Connection settings and Flyway migrations. |
| `application/SupplyChainReader.java` | Semantic reads with method references (the Java twin of the C# lambda reader). |
| `application/SupplyChainCrud.java` | Create/read/update/delete for the three entities, authorization-gated. |
| `src/main/resources/db/migration/V20261114161434__InitialCreate.sql` | Generated Flyway schema migration. |
| `src/main/foundgine/migrations/model-snapshot.json` | Snapshot of the fluent persistence model used for drift detection. |
| `src/main/foundgine/migrations/history/` | Generator history and rollback metadata. |
| `migrations.sh` / `migrations.cmd` | Cross-platform wrappers for the EF-style migration CLI. |
| `foundgine-migrations.properties` | Model class and demo-data location for the CLI. |

## Reads with method references

```java
var reader = new SupplyChainReader(orm.reader(em), caller);
var rows = reader.from(Supplier.class)
        .eq(Supplier::country, "US")
        .select(Supplier::id, Supplier::name, Supplier::riskScore)
        .orderBy(Supplier::name, SemanticSortDirection.ASC)
        .rows();
```

Java lambdas expose no expression trees, so only getter/record-accessor **method references** are accepted (a plain
lambda is rejected). Each becomes a semantic field name. Every read goes through authorize → plan → ExecutionIR →
JPA, so the caller's tenant predicate and field rules apply whatever the filter says.

## CRUD

`SupplyChainCrud` wraps the generic `JpaEntities` gateway:

- **Read/list** are gated by Foundgine (role, tenant, field rules).
- **Create/update/delete** need `Authorization.canWrite` (a writing role and not read-only). Created
  suppliers/warehouses are stamped with the caller's tenant; `tenantId` cannot be changed; update/delete affect only
  rows the caller can read (0 rows otherwise).

## Migrations (EF-style model-derived migrations)

The starter is the reference sample for `foundgine-migrations`. The fluent persistence model in
`model/PersistenceConfigurations.java` is the schema source of truth. As in the Advanced sample,
`SimplePhysicalSchemaModel` projects only physical persistence declarations (not semantic
configurations or entity maps). The standard migrations CLI uses that projection via
`foundgine-migrations.properties`, diffs it against the committed model snapshot, and writes a
versioned Flyway migration plus rollback SQL and an updated snapshot. `SimpleMigrationGenerator`
provides the same direct deterministic-DDL path as `AdvancedMigrationGenerator`; it writes to
`target/generated-migrations` by default and never overwrites the checked-in Flyway migration.

From this sample directory:

```bash
# Add a migration after changing the fluent persistence model
./migrations.sh migrations add AddSupplierEmail

# Review migration SQL and rollback SQL, then inspect model drift
./migrations.sh migrations list
./migrations.sh migrations has-pending-model-changes

# Apply pending Flyway migrations and inspect database history
./migrations.sh database update
./migrations.sh database status

# Print the SQL after a given version (omit the version for the full script)
./migrations.sh migrations script 20261114161434

# Remove the newest, unapplied migration and restore its prior snapshot
./migrations.sh migrations remove
```

On Windows, use `migrations.cmd` with the same arguments. The CLI reads
`foundgine-migrations.properties`; the model class is configured there, while connection settings
can be supplied through `FOUNDGINE_JDBC_URL`, `FOUNDGINE_JDBC_USER` and
`FOUNDGINE_JDBC_PASSWORD` (the wrapper also accepts the sample's `SUPPLYCHAIN_*` variables).

Generated files are written under `src/main/resources/db/migration`, with rollback scripts and
snapshot/history under `src/main/foundgine/migrations`. **Always review generated SQL before
applying it.** Drops and other destructive changes produce warnings; the generated rollback is a
review aid, not a substitute for a tested backup/restore plan.

Hibernate remains in validate-only mode and does not create or mutate the schema. Demo data is
separate from versioned schema migrations and is configured as a repeatable Flyway location.
Existing databases initialized from the former handwritten V1 have a different Flyway history;
for this sample, recreate the local development database volume before using the generated
baseline (for example, `docker compose down -v` followed by `docker compose up -d`).

The starter currently declares three tables, primary keys and indexes. The shared compiler and
PostgreSQL schema pipeline also support identity, defaults, foreign keys, CHECK constraints,
PostgreSQL extensions and index methods/operator classes; add those declarations to the fluent
persistence configurations when the starter schema needs them. The committed migration remains
unchanged until generated SQL has been reviewed and verified against PostgreSQL 16.

## Run

```bash
cd src/java/samples/foundgine-supply-chain
docker compose up -d          # empty PostgreSQL on localhost:4429; the app migrates it
cd ../..
mvn -pl samples/foundgine-supply-chain -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.Main
mvn -pl samples/foundgine-supply-chain -am test      # no database needed
```

Connection settings: `SUPPLYCHAIN_JDBC_URL`, `SUPPLYCHAIN_JDBC_USER`, `SUPPLYCHAIN_JDBC_PASSWORD`. If PostgreSQL is not
reachable, `Main` prints a "JPA demo skipped" line instead of failing.

## Tests

`SimpleSchemaModelParityTest` locks the three-table physical projection and its key columns/indexes;
`SimpleMigrationGeneratorTest` verifies deterministic SQL and explicit output paths. `SupplyChainOrmTest`
(fluent configuration, fail-closed validation, tenant isolation, field authorization, plan fingerprints,
against the in-memory ORM source) and `SupplyChainApplicationTest` (method-reference names, write gate)
cover the application path.

## Next sample

For the complete MCP and mutation path, see [`../foundgine-supply-chain-advanced`](../foundgine-supply-chain-advanced/README.md).
