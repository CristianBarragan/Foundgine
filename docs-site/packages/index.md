# Foundgine packages

The .NET package family is organized around four architectural packages. Optional integrations and tooling are separate artifacts:

| Package                | Responsibility                                                                |
| ---------------------- | ----------------------------------------------------------------------------- |
| `Foundgine.Core`       | Semantic model, metadata, intent, planning and provider-independent contracts |
| `Foundgine.Runtime`    | Application-facing orchestration, authorization and execution                 |
| `Foundgine.Providers`  | Concrete .NET storage (SQL, EF Core, in-memory, search), events, gateway, AI/model, MCP and AOT |
| `Foundgine.Extensions` | Optional GraphQL (Hot Chocolate), Keycloak identity and Temporal workflow integrations |

The .NET `Foundgine.Providers` package includes the Foundgine-owned ORM and EF Core provider assemblies and both source generators. EF Core 10 and its relational / DI dependencies are restored through NuGet dependency declarations; third-party DLLs are not bundled in the Foundgine package.

The Java artifacts use the `io.github.cristianbarragan` groupId and the repository has a Maven Central publishing configuration. The source POM version is 3.3.5; verify the exact artifact/version is available in Central before pinning it, because the publishing configuration alone is not publication evidence.

| Java module | Purpose | Audit status |
| --- | --- | --- |
| `foundgine-core`, `foundgine-runtime`, `foundgine-providers`, `foundgine-extensions` | Main server implementation | Source and tests present; full Maven verification for this checkpoint remains pending |
| `foundgine-migrations` | PostgreSQL schema diff and Flyway migration generation | Source and tests present; Maven/runtime verification pending; see [module README](https://github.com/CristianBarragan/Foundgine/blob/main/src/java/foundgine-migrations/README.md) |
| `foundgine-spring` | Spring Boot integration | Source and tests present; dependency pins/runtime verification pending |
| `foundgine-langchain4j` | LangChain4j integration | Source and tests present; dependency pins/runtime verification pending |
| `foundgine-aot-generator` | Build-time annotation processor module | Not published as a standalone runtime artifact |

The Central publishing configuration includes the Maven artifacts above except the build-time-only `foundgine-aot-generator`:

```xml
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-runtime</artifactId>
  <version>3.3.5</version>
</dependency>
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-providers</artifactId>
  <version>3.3.5</version>
</dependency>
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-extensions</artifactId>
  <version>3.3.5</version>
</dependency>
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-migrations</artifactId>
  <version>3.3.5</version>
</dependency>
```

`foundgine-core` is brought in transitively by the higher-level artifacts; add it directly when your code targets its contracts. `foundgine-extensions` is available the same way for optional framework integrations.

The .NET `Foundgine.Providers.AgentFramework` adapter is a separate packable project in the current working tree. It is not part of the four-package base stack and has not yet been build-verified or released; do not depend on a published version yet.

For installation and package-level details, see the project READMEs under [`src/`](https://github.com/CristianBarragan/Foundgine/tree/main/src).

## Continue

[Samples](../samples/index.html) → [Getting started](../getting-started/index.html)
