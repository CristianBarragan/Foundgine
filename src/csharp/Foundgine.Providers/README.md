# Foundgine.Providers

## Purpose

`Foundgine.Providers` contains concrete integrations that connect Foundgine's provider-independent semantic plans to application infrastructure. It is the package to use when your application needs actual storage, model/tool, MCP, or related provider implementations.

## What this package provides

The current package consolidates the provider surface into one consumer-facing package, including the relevant implementations under areas such as:

- Storage: SQL and in-memory providers.
- PostgreSQL-backed durable warrant revocation epoch anchor, explicit schema migrator, and readiness health check.
- Model/AI provider descriptors, MCP tooling, EventStore, outbox and gateway.
- Separate optional packages next to this one: `Foundgine.Providers.Redis` (warrant replay and MCP sessions for Redis, Valkey and Garnet), `Foundgine.Providers.AgentFramework` (Microsoft Agent Framework), `Foundgine.Providers.PostgresVector` (pgvector) and `Foundgine.Providers.Elasticsearch`.
- MCP/tool provider integrations.
- ORM execution through the shared `Foundgine.Providers.Orm` layer and the Entity Framework Core adapter.
- AOT-friendly metadata support.
- The `Foundgine.Providers.Aot.Generator` and `Foundgine.Providers.Orm.Generator` Roslyn source generators as **build-time analyzers**.

`Foundgine.Providers` brings in `Foundgine.Core` and `Foundgine.Runtime`, plus the NuGet dependencies required by the included provider integrations. In particular, EF Core 10 and its relational / dependency-injection abstractions are restored as NuGet dependencies; their third-party DLLs are not bundled in the package. Hot Chocolate GraphQL translation _and_ secure query/mutation execution both live in `Foundgine.Extensions`, not here — add that package separately if your application uses GraphQL.

The PostgreSQL warrant revocation epoch anchor is registered with `AddFoundginePostgresWarrantRevocationEpochAnchor(dataSource)` after `PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource)` has completed. The configured data source must target the writable primary.

The exact provider implementation you use determines which external infrastructure and configuration your application needs. Installing this package restores dependencies for the included integrations (EF Core, Npgsql, Microsoft.Extensions.AI and ModelContextProtocol). Redis, Agent Framework, pgvector and Elasticsearch client libraries come only with their own packages.

## AOT generator behavior

Both Foundgine generators are intentionally **not** separate NuGet packages and are not runtime dependencies. They are packaged under the NuGet analyzer path and are invoked by the consuming project's build when applicable:

```text
NuGet install
    ↓
Foundgine.Providers
    ├── runtime provider assemblies (Providers, Orm, EntityFrameworkCore)
    ├── analyzers/dotnet/cs/Foundgine.Providers.Aot.Generator.dll
    └── analyzers/dotnet/cs/Foundgine.Providers.Orm.Generator.dll
                 ↓
          consumer build
                 ↓
          generated C#
                 ↓
          compiled application
```

Neither generator runs continuously at application runtime or should be deployed as an application runtime assembly. `dotnet publish` may invoke it during compilation, but the deployed application contains the compiled generated result, not the generator itself.

## What is expected from the consumer

Install this package when you need one or more concrete providers. Configure the provider-specific dependencies (for example a database connection or external service), compose your semantic model and authorization policy, and register `Foundgine.Runtime`. The package does not automatically know your domain, database schema, credentials, tenancy rules or business authorization policy.

For AOT and ORM map generation, no separate generator installation or package reference is required when `Foundgine.Providers` is referenced normally.

## Install

```bash
dotnet add package Foundgine.Providers --version 3.4.0
```

## Typical application stack

`Foundgine.Core` defines the semantic contracts → `Foundgine.Runtime` controls execution → `Foundgine.Providers` connects the plan to concrete infrastructure. Add `Foundgine.Extensions` only when the application needs its optional framework integrations such as Hot Chocolate.


## Foundgine Gateway

The optional ASP.NET Core gateway facade is included in this package under `Foundgine.Providers.Gateway`. It exposes capability/semantic discovery, protected semantic read and mutation workflows, opaque approval handles, execution status, replayable SSE notifications, provider lifecycle hooks, and diagnostics. Its default event and approval stores are process-local development implementations; production hosts must provide durable distributed adapters. See [`Gateway/README.md`](Gateway/README.md) for the HTTP contract, event-store requirements, AOT serialization limits, security requirements, sample host, and tests.


## Optional adapters (separate packages)

- [`Foundgine.Providers.Redis`](../Foundgine.Providers.Redis/README.md): shared warrant replay protection and MCP session storage using atomic Redis operations (Redis, Valkey, Garnet).
- [`Foundgine.Providers.AgentFramework`](../Foundgine.Providers.AgentFramework/README.md): Microsoft Agent Framework adapter exposing governed Foundgine tools.
- [`Foundgine.Providers.PostgresVector`](../Foundgine.Providers.PostgresVector/README.md) and [`Foundgine.Providers.Elasticsearch`](../Foundgine.Providers.Elasticsearch/README.md): lexical-grounding candidate sources.

