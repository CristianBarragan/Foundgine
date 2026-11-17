# Foundgine packages

The .NET package family is organized around four architectural packages (client SDKs for TypeScript, Python and WebAssembly are listed [below](#client-sdks-typescript-python-and-webassembly)). The provider packages contain the concrete infrastructure and framework adapters; larger GraphQL, Keycloak and Temporal integrations remain in Extensions:

| Package                | Responsibility                                                                |
| ---------------------- | ----------------------------------------------------------------------------- |
| `Foundgine.Core`       | Semantic model, metadata, intent, planning and provider-independent contracts |
| `Foundgine.Runtime`    | Application-facing orchestration, authorization and execution                 |
| `Foundgine.Providers`  | Concrete .NET storage (SQL, EF Core, in-memory, search), Redis shared state, Microsoft Agent Framework, events, gateway, AI/model, MCP and AOT |
| `Foundgine.Extensions` | Optional GraphQL (Hot Chocolate), Keycloak identity and Temporal workflow integrations |

The .NET `Foundgine.Providers` package includes the Foundgine-owned ORM and EF Core provider assemblies, Redis and Microsoft Agent Framework adapters, and both source generators. EF Core 10 and its relational / DI dependencies are restored through NuGet dependency declarations; third-party DLLs are not bundled in the Foundgine package.

The Java artifacts use the `io.github.cristianbarragan` groupId and the repository has a Maven Central publishing configuration. The source POM version is 3.4.0; verify the exact artifact/version is available in Central before pinning it, because the publishing configuration alone is not publication evidence.

| Java module | Purpose | Audit status |
| --- | --- | --- |
| `foundgine-core`, `foundgine-runtime`, `foundgine-providers`, `foundgine-extensions` | Main server implementation; `foundgine-providers` also contains Redis, Spring Boot and LangChain4j adapters | Source and tests present; full Maven verification for this checkpoint remains pending |
| `foundgine-migrations` | PostgreSQL schema diff and Flyway migration generation | Source and tests present; Maven/runtime verification pending; see [module README](https://github.com/CristianBarragan/Foundgine/blob/main/src/java/foundgine-migrations/README.md) |
| `foundgine-aot-generator` | Build-time annotation processor module | Not published as a standalone runtime artifact |

The Central publishing configuration includes the Maven artifacts above except the build-time-only `foundgine-aot-generator`:

```xml
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-runtime</artifactId>
  <version>3.4.0</version>
</dependency>
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-providers</artifactId>
  <version>3.4.0</version>
</dependency>
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-extensions</artifactId>
  <version>3.4.0</version>
</dependency>
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-migrations</artifactId>
  <version>3.4.0</version>
</dependency>
```

`foundgine-core` is brought in transitively by the higher-level artifacts; add it directly when your code targets its contracts. `foundgine-extensions` is available the same way for optional framework integrations.

The .NET `Foundgine.Providers` package includes Redis and Microsoft Agent Framework adapters. Java `foundgine-providers` includes Redis, Spring Boot and LangChain4j adapters; Spring Boot and LangChain4j dependencies are optional.

For installation and package-level details, see the project READMEs under [`src/`](https://github.com/CristianBarragan/Foundgine/tree/main/src).

## Agent framework and Spring integrations

Optional integrations are included in the provider packages: Microsoft Agent Framework and Redis in .NET `Foundgine.Providers`; LangChain4j, Spring Boot and Redis in Java `foundgine-providers`. See [Agent and framework integrations](https://github.com/CristianBarragan/Foundgine/blob/main/docs/wiki/agent-framework-integrations.md).

## Client SDKs: TypeScript, Python and WebAssembly

Three thin transport clients talk to a running Foundgine server over MCP (Streamable HTTP, default path `/mcp`) and the Temporal workflow routes under `/foundgine/workflows`. They are not ports of the semantic runtime: identity, tenant, authorization, planning and providers stay on the .NET or Java server.

| SDK | Package | Runtime | Source |
| --- | --- | --- | --- |
| TypeScript | `@foundgine/typescript` (npm) | Node.js 20+ or any runtime with `fetch` | [`src/typescript`](https://github.com/CristianBarragan/Foundgine/tree/main/src/typescript) |
| Python | `foundgine-sdk` (PyPI name) | Python 3.11+, `httpx`; async and sync clients | [`src/python`](https://github.com/CristianBarragan/Foundgine/tree/main/src/python) |
| Rust / WebAssembly | `@foundgine/wasm` (npm) | Node.js 20+, browsers, Workers, Deno; host supplies `fetch` | [`src/wasm`](https://github.com/CristianBarragan/Foundgine/tree/main/src/wasm) |

All three are development packages, not release-ready SDKs; the repository does not claim a registry release, so verify that a version exists on npm or PyPI before pinning it. See the [client SDK overview](https://github.com/CristianBarragan/Foundgine/blob/main/docs/wiki/client-sdks.md) for a comparison, a quick start and the verification status.

## Continue

[Samples](../samples/index.html) → [Getting started](../getting-started/index.html)
