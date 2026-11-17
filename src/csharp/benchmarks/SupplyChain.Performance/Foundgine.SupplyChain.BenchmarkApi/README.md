# Foundgine SupplyChain benchmark API

This is the Foundgine C# arm of the SupplyChain benchmark. It exposes the same GraphQL request surface as the independent Hot Chocolate baseline at `POST /graphql`.

## Required configuration

- `ConnectionStrings:SupplyChain` — benchmark PostgreSQL database.
- `Foundgine:Keycloak:Authority` — Keycloak realm issuer URL.
- `Foundgine:Keycloak:Audience` — `supplychain-api`.
- `Foundgine:Keycloak:TenantClaim` — `tenant_id`.
- `Foundgine:Keycloak:WarrantPrivateKeyPemPath` — mounted RSA private key (2048-bit minimum).
- `Foundgine:Keycloak:RoleGrants` — role-to-capability grants. Start from `appsettings.example.json`; its role names match the shared benchmark realm.
- `Redis:ConnectionString` — Redis 7.4 shared revocation store (`benchmark:csharp:` namespace).
- `ConnectionStrings:SupplyChain` also supplies the PostgreSQL durable revocation epoch anchor; its idempotent migrator runs before accepting requests.

The host issues warrants only from the validated Keycloak principal. The tenant and customer identifiers are not accepted from GraphQL variables as authority claims. Customer order reads are constrained by a semantic `CustomerId` predicate whose right-hand value is supplied through the execution context from the validated `customer_id` claim; it is emitted as a bound SQL parameter rather than injected into GraphQL text. Customer order creation must target that same identifier.

## Mutation provider lifetime

The host registers the same `MetadataRegistry` instance as both `IMutationSchema` and the Foundgine metadata catalog. `AddFoundgine` creates the singleton `IFoundgineMutations` engine from that schema and the configured provider factory. The Foundgine mutation engine is singleton-scoped. `FactoryEfCoreMutationExecutionProvider` uses `IDbContextFactory<FoundgineDbContext>` to create and dispose a fresh context for each mutation batch, then delegates to `EfCoreMutationExecutionProvider` so the complete batch retains its provider transaction and conformance checks.

## Migrations

This host owns the `Foundgine.SupplyChain.BenchmarkApi` migration assembly. Generate its independent migration from the repository root with `tools/foundgine-perf/csharp/generate-foundgine-migration.sh`. Do not use the sample project's migration assembly.

Warrant revocation uses `RedisSecurityWarrantRevocationStore` wrapped by `AnchoredSecurityWarrantRevocationStore` and `PostgresSecurityWarrantRevocationEpochAnchor`. Successful revocations are anchored before acknowledgement; Redis rollback below the durable high-water mark, standby reads, or anchor outages fail closed. The `/health/ready` endpoint checks the Redis write/read/delete probe and anchored sequence. `MemorySecurityWarrantReplayStore` remains process-local and is intentionally limited to the single-instance benchmark deployment; replace it with a shared atomic replay store before scaling the host horizontally.

## Authorization regression tests

`SupplyChainWarrantAuthorizationTests` covers warrant tenant binding, role-to-capability denial, customer owner predicates, missing-customer-claim fail-closed behavior, and the absence of customer-only row filtering for operators. Run it with:

```bash
dotnet test src/csharp/tests/Foundgine.GraphQL.HotChocolate.Tests/Foundgine.GraphQL.HotChocolate.Tests.csproj --filter FullyQualifiedName~SupplyChainWarrantAuthorizationTests
```

These are source-level/unit acceptance tests. They do not replace HTTP-to-PostgreSQL tests with two real tenants and signed Keycloak tokens.

## Current verification boundary

The benchmark API source wiring and Python source-contract checks are in place. .NET compilation and live Redis/PostgreSQL/Keycloak verification remain outstanding. Run `verify-migration-provenance.sh`, build the API and test project, and execute the HTTP tenant-isolation/role vectors against PostgreSQL and Keycloak before treating this arm as complete.
