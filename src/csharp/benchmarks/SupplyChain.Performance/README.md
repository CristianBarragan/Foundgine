# C# SupplyChain benchmark arms

The C# benchmark has two independent HTTP GraphQL hosts:

- **Baseline:** Hot Chocolate + ordinary EF Core + Npgsql. It uses its own `SupplyChainDbContext`, Keycloak JWT validation, GraphQL roots, and baseline migration assembly.
- **Foundgine:** Foundgine GraphQL query/mutation executors + Foundgine EF Core provider. It uses `SupplyChainDomain.Metadata`, the same fluent storage mapping, a separate `FoundgineDbContext` migration assembly, and Keycloak-issued signed warrants.

Both hosts use the same benchmark schema contract and deterministic seed SQL. They retain separate migration histories and databases, but share the same Redis service and C# Redis namespace so the baseline is not measured without the Foundgine arm’s security-state dependency.

## Foundgine host composition

`Foundgine.SupplyChain.BenchmarkApi` now:

- Registers the EF query provider with `UseEngineProvider()`.
- Registers `MetadataRegistry` as the actual `IMutationSchema`.
- Creates an `EfCoreMutationExecutionProvider` from `IDbContextFactory<FoundgineDbContext>` for each atomic mutation batch. The singleton Foundgine mutation engine never captures a scoped `DbContext`.
- Registers `IFoundgineMutations` through `FoundgineOptions.MutationProviderFactory`.
- Registers `FoundgineGraphQLQueryExecutor` and `FoundgineGraphQLMutationExecutor` behind `POST /graphql`.
- Uses Keycloak JWT validation as the only HTTP identity source. The Keycloak extension maps `sc-customer`, `sc-operator`, `sc-analyst`, and `sc-manager` roles into short-lived signed warrants. The runtime verifies issuer, audience, expiry, tenant, capability and operation before execution.
- Requires a `tenant_id` claim and uses the trusted `customer_id` claim to scope customer order reads and validate customer order creation. Neither value is accepted from GraphQL input as an authority source.
- Reassembles the query executor's flat provider rows/cells into nested GraphQL selections, preserving aliases and de-duplicating collection relationships by identity.

The benchmark host now uses Redis-backed warrant revocation with a PostgreSQL durable high-water anchor. The Redis key namespace is `benchmark:csharp:` and is shared with the Hot Chocolate baseline; both fail closed on Redis rollback or anchor unavailability. The replay store remains process-local (`MemorySecurityWarrantReplayStore`) for this single-instance benchmark profile. The Hot Chocolate baseline uses direct StackExchange.Redis and Npgsql code for the same Redis sequence, PostgreSQL durable anchor, primary checks, read-after-write verification, and readiness round trip; it does not reference Foundgine provider assemblies. The example signing key must be supplied as a mounted secret; never use an ephemeral key for benchmark acceptance.

## Independent migrations

The Foundgine host uses migration assembly `Foundgine.SupplyChain.BenchmarkApi` and has its own design-time factory. Generate its migration with:

```bash
./tools/foundgine-perf/csharp/generate-foundgine-migration.sh
```

Generate the plain baseline migration separately with:

```bash
./tools/foundgine-perf/csharp/generate-baseline-migration.sh
```

Do not copy the sample project's existing migration into either benchmark host. The benchmark hosts must each own their migration source and history.

## Verification status

Source-level wiring checks are in `tools/foundgine-perf/tests/test_foundgine_csharp_arm.py`. They verify registration, Keycloak role-to-capability mappings, customer claim handling, factory-backed mutation provider wiring, and the independent migration assembly/design-time factory.

The source checks do **not** prove .NET compilation, live Redis/PostgreSQL anchor behavior, live Keycloak token validation, PostgreSQL migration application, HTTP response equivalence, or concurrent tenant isolation. Those remain acceptance gates and must pass before running performance measurements. In particular, the .NET 10 SDK and PostgreSQL/Keycloak services are required to complete those runtime checks.
