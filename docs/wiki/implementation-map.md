# Cross-language implementation map

This page maps capabilities to the repository's current module layout. It is an orientation guide, not a guarantee that every framework adapter or provider has identical feature coverage in C# and Java.

| Capability | C# location | Java location |
|---|---|---|
| Semantic contracts, types, identities | `src/csharp/Foundgine.Core/Semantic`, `Interfaces` | `src/java/foundgine-core/.../com/foundgine/core/semantic` |
| Model configuration and mapping | `Foundgine.Core/Semantic/Configuration` | `foundgine-core` semantic configuration packages |
| Intent and lexical grounding | `Foundgine.Core/Semantic/Intent`, `Resolution` | `foundgine-core` intent/resolution packages |
| Query and mutation planning | `Foundgine.Core/Semantic/Query`, `Mutation`, `Aggregates` | `foundgine-core` query/mutation/planning packages |
| Runtime orchestration and APIs | `src/csharp/Foundgine.Runtime` | `src/java/foundgine-runtime` |
| ORM providers (EF Core / JPA) | `src/csharp/Foundgine.Providers.Orm`, `Foundgine.Providers.EntityFrameworkCore`, `Foundgine.Providers.Orm.Generator` | `foundgine-providers/.../storage/orm` and `.../orm/jpa` |
| SQL, in-memory and retrieval providers | `src/csharp/Foundgine.Providers/Storage` | `src/java/foundgine-providers` |
| Schema migrations | EF Core migration tooling; no matching Foundgine migration module | `src/java/foundgine-migrations` (PostgreSQL schema diff and Flyway SQL; source/tests present, Maven/runtime verification pending) |
| Gateway, event and outbox | `Foundgine.Providers/Gateway`, `EventStore`, `Outbox` | `foundgine-providers` gateway/event/outbox packages |
| MCP and agent tooling | `Foundgine.Providers/Tools/MCP`, runtime control plane | `foundgine-providers` tool/MCP packages and `foundgine-runtime` governance |
| AI model adapters | `Foundgine.Providers.Models` (Microsoft.Extensions.AI); separate `Foundgine.Providers.AgentFramework` project is not build-verified | `foundgine-langchain4j` (source/tests present; dependency/runtime verification pending) |
| Spring Boot integration | N/A | `foundgine-spring` (source/tests present; dependency/runtime verification pending) |
| AOT metadata | `Foundgine.Providers/Aot`, `Foundgine.Providers.Aot.Generator` | `foundgine-aot-generator` and provider annotations/metadata |
| Keycloak | `src/csharp/Foundgine.Extensions/Keycloak` | `src/java/foundgine-extensions/.../keycloak` |
| Temporal | `src/csharp/Foundgine.Extensions/Temporal` | `src/java/foundgine-extensions/.../temporal` |
| GraphQL adapter | `src/csharp/Foundgine.Extensions/GraphQL/HotChocolate` | `src/java/foundgine-extensions/src/main/java/com/foundgine/extensions/graphql` (GraphQL Java; different API, no Hot Chocolate parity claim) |
| Samples and tests | `src/csharp/samples`, `tests`, `security` | `src/java/samples`, module tests, `security` |
| Client SDK for a running server (TypeScript) | N/A (server) | N/A (server); client lives in `src/typescript` (`@foundgine/typescript`) |
| Client SDK (Python) | N/A (server) | N/A (server); client lives in `src/python` (`foundgine-sdk`) |
| Client SDK (Rust / WebAssembly) | N/A (server) | N/A (server); client lives in `src/wasm` (`@foundgine/wasm`) |

## Module responsibilities

### Core

Owns provider-independent semantic vocabulary, identities, validation, resolution/planning contracts, and security primitives. Core should not depend on application-specific generated classes, a concrete database, or a transport framework.

### Runtime

Coordinates semantic requests, authorization, plans, execution lifecycle, control-plane governance, and hosting abstractions. Runtime should not become a second application service layer that embeds vendor-specific storage behavior.

### Providers

Implements physical execution and infrastructure adapters, including the SQL, **EF Core** and **JPA** execution providers (see [ORM providers](orm-providers) and the [Provider catalogue](providers)). Provider capabilities must be declared and validated; provider-specific semantics must not leak into core contracts.

### Extensions

See [Extensions](extensions) for the single catalogue. Adapts third-party frameworks and services. Extensions should translate to/from Foundgine contracts and delegate the actual semantic and security decisions to the appropriate core/runtime services. Optional Spring Boot and LangChain4j Java modules have source and test definitions, but dependency pins and runtime verification remain pending. The separate C# Microsoft Agent Framework adapter is not included in the main solution and has not been build-verified.

### Framework integrations

`Foundgine.Providers.AgentFramework` (Microsoft Agent Framework, C#), `foundgine-langchain4j` and `foundgine-spring` (Java) are optional adapters; see [Agent and framework integrations](agent-framework-integrations) for scope and verification status.

### Client SDKs

`src/typescript/`, `src/python/` and `src/wasm/` are not implementations of the semantic runtime and have no C#/Java feature parity to track. They are transport clients for the MCP endpoint and the Temporal workflow routes that the C# and Java servers expose. See [Client SDKs](client-sdks).

### Migrations

Java's `foundgine-migrations` module is developer tooling, not a runtime provider. It derives a PostgreSQL schema from Foundgine metadata, diffs it against a committed JSON snapshot and generates Flyway SQL plus rollback/history files. Its source and tests are present, but full Maven/runtime verification remains pending. It does not infer renames, partial/expression indexes, triggers, views, generated columns or data backfills. Review generated SQL before applying it; see the [module README](https://github.com/CristianBarragan/Foundgine/blob/main/src/java/foundgine-migrations/README.md). .NET applications continue to use EF Core's migration tooling.

## How to verify a component

Search for the public interface/record/class in the relevant module, inspect its registration extension, and follow the call path into the implementation. Then locate tests that cover both success and denial/failure behavior. The wiki intentionally avoids reproducing every method signature because those change with the source and should be read from the current code or generated API documentation.
