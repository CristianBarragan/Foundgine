# C# baseline — Hot Chocolate + ordinary EF Core

This is the normal C# implementation arm. It uses its own `SupplyChainDbContext`, Npgsql provider, JWT validation, Hot Chocolate resolvers and EF Core migration assembly. It deliberately links only the plain SupplyChain POCO domain types from the sample; it does not reference Foundgine packages. For a fair security-infrastructure comparison it uses direct StackExchange.Redis and Npgsql code for the same Redis-backed revocation sequence and PostgreSQL durable high-water anchor as the Foundgine arm.

## Configuration

Required environment variables (the compose file supplies them):

- `ConnectionStrings__SupplyChain`
- `Keycloak__Authority` (exact realm issuer)
- `Keycloak__Audience` (defaults to `supplychain-api`)
- `Keycloak__RequireHttpsMetadata`
- `Database__MigrateOnStart=true`
- `Redis__ConnectionString` — shared Redis 7.4 service (`benchmark:csharp:` namespace).

## EF Core migration provenance

Generate the baseline migration with the EF Core CLI from this project, not with Foundgine tooling:

```bash
dotnet tool install --global dotnet-ef --version 10.0.0
dotnet ef migrations add Baseline \
  --project src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi \
  --startup-project src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi
```

The migration must be committed under this project before building the benchmark image. At startup the application calls `Database.MigrateAsync`; the benchmark-only security epoch table is initialized separately by `BaselineRevocationEpochGuard.Migrate`. It never calls `EnsureCreated` or `EnsureDeleted`. The `/health/ready` endpoint checks PostgreSQL, performs a Redis write/read/delete probe, and verifies the durable high-water mark. GraphQL requests bracket identity and digest revocation lookups with sequence checks. Redis/anchor failures and sequence rollback fail closed. The harness applies the shared deterministic seed SQL after migration.

## Current limitation

The EF-generated migration is checked in, but .NET compilation and live PostgreSQL/Redis/Keycloak verification have not been run in this environment. Do not treat this arm as accepted until the build, migration-history, schema-parity, authorization and response-equivalence gates pass.
