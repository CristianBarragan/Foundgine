# SupplyChain benchmark implementation checkpoint

Date: 2026-11-17

## Scope and source of truth

This checkpoint is based on the supplied Foundgine repository ZIP, the
`foundgine-perf-supplychain-increment.zip`, and the supplied
`Foundgine-e2e-example(1).zip`. The benchmark remains SupplyChain; the CRM/ERP
repository is an integration-pattern reference only.

## Confirmed existing implementation foundations

- C# sample: `src/csharp/samples/Foundgine.SupplyChain` contains the domain,
  semantic model, EF Core integration, `DatabaseMigrator`, and a checked-in
  `InitialSupplyChain` migration set.
- Java sample: `src/java/samples/foundgine-supply-chain` contains Supplier,
  Warehouse and Product JPA entities, `FoundgineOrm` configuration, and the
  `migrations.sh` / `migrations.cmd` entry points for `foundgine-migrations`.
- The benchmark increment defines four isolated database names, four service
  names/ports, two Keycloak-backed runtime configs, scenario files, seed SQL,
  authorization vectors, and schema/migration provenance checks.
- The E2E example is the reference for Keycloak claim extraction and tenant
  authorization patterns; it is not a substitute for SupplyChain entities.

## Missing benchmark applications

The supplied source trees do not contain these four projects, although
`compose/supplychain.yml` already points at them:

- `src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi`
- `src/csharp/benchmarks/SupplyChain.Performance/Foundgine.SupplyChain.BenchmarkApi`
- `src/java/benchmarks/supply-chain/spring-baseline`
- `src/java/benchmarks/supply-chain/foundgine`

The baseline migration artifacts are also absent: the C# EF Core `Baseline`
migration and Java `V1__baseline.sql`. The existing C# sample migration is
not evidence that `dotnet ef` generated it from the final benchmark project;
the benchmark's own documentation explicitly requires regeneration and a
clean pending-model-changes check. The Java Foundgine migration is described
as generated output, but it still needs to be re-generated/verified through
the checked-in migration CLI and applied to a clean benchmark database.

## Test results from this workspace

Command:

```bash
cd tools/foundgine-perf
python3 -m unittest tests.test_supplychain_wiring tests.test_schemaparity tests.test_armauthz -v
```

Result: **15 tests passed**. These are Python-level wiring, authorization
logic, and schema-provenance unit tests. They do not compile or execute any
of the four application arms and do not prove live PostgreSQL parity.

The full `unittest discover -s tests` run executes 64 tests and reports five
errors in `test_orm_pairs.py`. All five are `FileNotFoundError` for the separate
CoffeeBeanery benchmark source tree under `perf/src/csharp/...`; that tree is
not part of the supplied SupplyChain increment. These are missing-fixture
errors, not SupplyChain test assertion failures. The CoffeeBeanery tests must
not be counted as SupplyChain readiness tests.

## Environment limitations

This workspace has OpenJDK 21.0.12.1. `dotnet`, `mvn`, and `docker` are not
installed. Consequently no .NET/Java compilation, EF/Flyway migration run,
Keycloak startup, PostgreSQL schema inspection, or GraphQL HTTP test can be
truthfully reported from this environment.

## Non-negotiable implementation gates

1. Implement all four application projects at the paths above.
2. Baseline C#: generate and apply its own EF Core `Baseline` migration.
3. Foundgine C#: regenerate `InitialSupplyChain` with `dotnet ef` against the
   Foundgine-derived EF model; verify no pending model changes; apply through
   the sample's `DatabaseMigrator`.
4. Baseline Java: apply its own hand-authored Flyway `V1__baseline.sql`.
5. Foundgine Java: generate the versioned SQL through `foundgine-migrations`
   from the fluent model, then apply it with Flyway.
6. Start all arms against separate empty PostgreSQL databases and import the
   same Keycloak realm. Apply the benchmark seed scripts only after each arm's
   migration completes.
7. Run `schemaparity`, `armauthz`, and response-equivalence probes for each
   runtime pair before performance measurements.
8. Only publish performance results after the readiness gates and repeated
   measurement sessions pass.

## Important interpretation

The C# and Java domains are intentionally different in this increment. Compare
baseline versus Foundgine **within each runtime**, using that runtime's own
scenarios. Do not compare C# and Java raw timings as a like-for-like ORM result.

## Continuation: Java baseline migration artifact

Added the plain Java arm's independent hand-authored migration at
`src/java/benchmarks/supply-chain/spring-baseline/src/main/resources/db/migration/V1__baseline.sql`.
It creates the same three tables (`products`, `suppliers`, `warehouses`), columns,
PostgreSQL types, primary keys and indexes as the existing Foundgine-generated
`V20261114161434__InitialCreate.sql`. Added `MIGRATION-PROVENANCE.md` to make the
separation explicit: baseline DDL is authored directly and applied by Flyway;
the Foundgine arm continues to generate its DDL with `foundgine-migrations`.

A new Python wiring test compares the table/column/type shape of those two SQL
artifacts. This is a static migration-artifact check only; it does not replace
live PostgreSQL schema parity or Flyway history verification.

The four benchmark application projects are still not implemented. The Java
baseline migration is one concrete implementation increment, not a runnable
benchmark arm. C# baseline EF migration generation and all four GraphQL services
remain outstanding.


## Continuation: Java Spring GraphQL arm scaffolding

Added the two Java application projects at the paths already referenced by Compose:

- `src/java/benchmarks/supply-chain/spring-baseline` — Spring GraphQL, Spring Security resource-server JWT validation, JPA/Hibernate mappings, and its independent hand-authored Flyway V1 migration. Hibernate is configured with `ddl-auto: validate`; Flyway owns DDL.
- `src/java/benchmarks/supply-chain/foundgine` — Spring GraphQL and Keycloak JWT validation around the existing `SupplyChainJpa` / `FoundgineOrm` sample. Reads use `FoundgineJpaReader`; warehouse mutation uses the Foundgine `JpaEntities` gateway after role and tenant ownership checks. Its Flyway location contains the sample's versioned Foundgine-generated migration artifact.

Both apps consume the same `tenant_id` and `realm_access.roles` claims from the supplied realm (`sc-customer`, `sc-analyst`, `sc-operator`, `sc-manager`) and expose the operation names used by the Java benchmark scenarios. Compose now passes the Foundgine JPA JDBC settings explicitly and disables sample demo seeding; the benchmark harness remains responsible for loading the deterministic seed SQL. Dockerfiles build the corresponding Maven reactor modules with Temurin 27.

The 21 focused Python wiring/schema/authz/application-scaffold tests pass after these additions. These tests do not compile Java or prove the runtime paths. This workspace still has JDK 21, not the project's configured Java 27, and no Maven or Docker, so the new applications remain **uncompiled and unexecuted** here. In particular, verify Spring Boot 4 GraphQL/security configuration, Foundgine AOT generated model availability, Flyway history and clean PostgreSQL startup, all scenario response equivalence, and the authorization attack vectors in a Java 27 + Maven + Docker environment.
