# Foundgine.Providers

## Purpose

`Foundgine.Providers` contains concrete integrations that connect Foundgine's provider-independent semantic plans to application infrastructure. It is the package to use when your application needs actual storage, model/tool, MCP, or related provider implementations.

## What this package provides

The current package consolidates the provider surface into one consumer-facing package, including the relevant implementations under areas such as:

- Storage: SQL, in-memory, Elasticsearch and PostgreSQL/vector integrations.
- Model/AI provider integrations, including the Microsoft Agent Framework adapter.
- Redis-compatible shared state: atomic warrant replay protection and shared MCP sessions for Redis, Valkey and Garnet.
- MCP/tool provider integrations.
- ORM execution through the shared `Foundgine.Providers.Orm` layer and the Entity Framework Core adapter.
- AOT-friendly metadata support.
- The `Foundgine.Providers.Aot.Generator` and `Foundgine.Providers.Orm.Generator` Roslyn source generators as **build-time analyzers**.

`Foundgine.Providers` brings in `Foundgine.Core` and `Foundgine.Runtime`, plus the NuGet dependencies required by the included provider integrations. In particular, EF Core 10 and its relational / dependency-injection abstractions are restored as NuGet dependencies; their third-party DLLs are not bundled in the package. Hot Chocolate GraphQL translation _and_ secure query/mutation execution both live in `Foundgine.Extensions`, not here — add that package separately if your application uses GraphQL.

The exact provider implementation you use determines which external infrastructure and configuration your application needs. Since the provider implementations ship together in one package, installing it restores dependencies for the included integrations (including EF Core, Npgsql, Pgvector, Microsoft.Extensions.AI, ModelContextProtocol, StackExchange.Redis and Microsoft.Agents.AI), even if your application uses only a subset.

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
dotnet add package Foundgine.Providers --version 3.3.5
```

## Typical application stack

`Foundgine.Core` defines the semantic contracts → `Foundgine.Runtime` controls execution → `Foundgine.Providers` connects the plan to concrete infrastructure. Add `Foundgine.Extensions` only when the application needs its optional framework integrations such as Hot Chocolate.


## Foundgine Gateway

The optional ASP.NET Core gateway facade is included in this package under `Foundgine.Providers.Gateway`. It exposes capability/semantic discovery, protected semantic read and mutation workflows, opaque approval handles, execution status, replayable SSE notifications, provider lifecycle hooks, and diagnostics. Its default event and approval stores are process-local development implementations; production hosts must provide durable distributed adapters. See [`Gateway/README.md`](Gateway/README.md) for the HTTP contract, event-store requirements, AOT serialization limits, security requirements, sample host, and tests.


## Redis provider

The Redis implementation lives under [`Redis/`](Redis/README.md). It provides shared warrant replay protection and MCP session storage using atomic Redis operations. It supports Redis-compatible servers including Valkey and Garnet and fails closed when the server is unavailable.

## Microsoft Agent Framework adapter

The Microsoft Agent Framework implementation lives under [`AgentFramework/`](AgentFramework/README.md). It exposes Foundgine capabilities and governed operations as agent tools. Mutation execution is disabled by default and remains human-approval-gated when enabled. Both integrations are compiled into the `Foundgine.Providers.dll` distributed by this package.
