# Foundgine.Providers

## Purpose

`Foundgine.Providers` contains concrete integrations that connect Foundgine's provider-independent semantic plans to application infrastructure. It is the package to use when your application needs actual storage, model/tool, MCP, or related provider implementations.

## What this package provides

The v2 package consolidates the provider surface into one package, including the relevant implementations under areas such as:

- Storage: SQL, in-memory, Elasticsearch and PostgreSQL/vector integrations.
- Model/AI provider integrations.
- MCP/tool provider integrations.
- AOT-friendly metadata support.
- The `Foundgine.Providers.Aot.Generator` Roslyn source generator as a **build-time analyzer**.

`Foundgine.Providers` depends only on `Foundgine.Core` and `Foundgine.Runtime`. Hot Chocolate GraphQL translation *and* secure query/mutation execution both live in `Foundgine.Extensions`, not here — add that package separately if your application uses GraphQL.

The exact provider implementation you use determines which external infrastructure and configuration your application needs. Note that installing this package brings in every provider's dependencies (including Npgsql, Pgvector, Microsoft.Extensions.AI and ModelContextProtocol) regardless of which provider(s) you actually use, since they currently ship as one package.

## AOT generator behavior

The AOT generator is intentionally **not** a separate NuGet package and is not a runtime dependency. It is packaged under the NuGet analyzer path and is invoked by the consuming project's build when applicable:

```text
NuGet install
    ↓
Foundgine.Providers
    ├── runtime provider assemblies
    └── analyzers/dotnet/cs/Foundgine.Providers.Aot.Generator.dll
                 ↓
          consumer build
                 ↓
          generated C#
                 ↓
          compiled application
```

The generator does not run continuously at application runtime and should not be deployed as an application runtime assembly. `dotnet publish` may invoke it during compilation, but the deployed application contains the compiled generated result, not the generator itself.

## What is expected from the consumer

Install this package when you need one or more concrete providers. Configure the provider-specific dependencies (for example a database connection or external service), compose your semantic model and authorization policy, and register `Foundgine.Runtime`. The package does not automatically know your domain, database schema, credentials, tenancy rules or business authorization policy.

For AOT generation, no separate generator installation or package reference is required when `Foundgine.Providers` is referenced normally.

## Install

```bash
dotnet add package Foundgine.Providers --version 2.0.3
```

## Typical application stack

`Foundgine.Core` defines the semantic contracts → `Foundgine.Runtime` controls execution → `Foundgine.Providers` connects the plan to concrete infrastructure. Add `Foundgine.Extensions` only when the application needs its optional framework integrations such as Hot Chocolate.
