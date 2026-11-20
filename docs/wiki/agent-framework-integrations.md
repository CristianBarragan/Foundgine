# Agent framework and Spring integrations

Foundgine ships its framework adapters inside the existing provider packages. They remain thin integrations: the framework handles conversation or hosting; Foundgine continues to resolve, authorize and execute every operation.

| Integration | Language | Package | Source and guide |
|---|---|---|---|
| Microsoft Agent Framework | C# / .NET | `Foundgine.Providers` (bundled adapter assembly) | [`Foundgine.Providers.AgentFramework/README.md`](../../src/csharp/Foundgine.Providers.AgentFramework/README.md) |
| Redis / Valkey / Garnet | C# / .NET | `Foundgine.Providers.Redis` | [`Redis/README.md`](../../src/csharp/Foundgine.Providers.Redis/README.md) |
| LangChain4j | Java | `io.github.cristianbarragan:foundgine-providers` | [`LANGCHAIN4J.md`](../../src/java/foundgine-providers/LANGCHAIN4J.md) |
| Spring Boot | Java | `io.github.cristianbarragan:foundgine-providers` | [`SPRING-BOOT.md`](../../src/java/foundgine-providers/SPRING-BOOT.md) |
| Redis / Valkey / Garnet | Java | `io.github.cristianbarragan:foundgine-providers` | Redis implementation under `com.foundgine.providers.security.redis` |

## Package dependencies

- In .NET, the Redis and Agent Framework adapter assemblies are bundled into the `Foundgine.Providers` NuGet package. Their source projects (`Foundgine.Providers.Redis` and `Foundgine.Providers.AgentFramework`) are separate, non-packable projects; the packaging project bundles their assemblies. NuGet restores `StackExchange.Redis` and `Microsoft.Agents.AI` as package dependencies.
- The Java `foundgine-providers` module includes Redis, LangChain4j and Spring Boot adapter source and Spring auto-configuration metadata. Its POM declares Lettuce, Spring Boot/Spring MVC and LangChain4j dependencies. Standalone `foundgine-spring` and `foundgine-langchain4j` module directories also exist, but they are not listed in the parent Maven reactor; do not assume those standalone artifacts are built or published from the normal reactor.
- The parent Maven reactor currently includes `foundgine-providers`; it does not include the standalone `foundgine-spring` or `foundgine-langchain4j` modules. Verify release artifacts before documenting standalone publication.

## Shared security model

- **Identity is host-owned.** Integrations resolve the host's security execution context at invocation time. The model cannot supply tenant, subject, warrant or authorization context.
- **Discovery is not authorization.** Tool discovery describes the available surface; every invocation is still resolved and authorized by Foundgine.
- **Writes remain governed.** Mutation tools are opt-in and approval-gated. Framework integration does not bypass Foundgine's plan validation or authorization.
- **Redis state fails closed.** Redis-backed warrant replay protection denies a request if the shared store cannot be reached; use persistent, appropriately replicated Redis-compatible infrastructure.

## Microsoft Agent Framework (.NET)

```csharp
builder.Services.AddFoundgineAgentFramework(options =>
{
    options.AgentName = "orders";
    options.EnableMutations = true; // still requires human approval
});
```

The adapter exposes Foundgine capability discovery, semantic/open-intent reads and mutation dry-run/execute tools. The `foundgine_mutation_execute` tool is wrapped for MAF approval; there is no model-callable approval tool. See the [adapter guide](../../src/csharp/Foundgine.Providers.AgentFramework/README.md).

Install the normal provider package:

```bash
dotnet add package Foundgine.Providers
```

## LangChain4j (Java)

```java
var assistant = AiServices.builder(Assistant.class)
        .chatModel(model)
        .tools(FoundgineLangChain4jTools.toolMap(server))
        .build();
```

Add `io.github.cristianbarragan:foundgine-providers`; add the LangChain4j framework dependency to the application when using this adapter. Tool execution still calls the Foundgine MCP server.

## Spring Boot (Java)

Spring auto-configuration is included in `foundgine-providers`. It can register the MCP server/controller and JPA integration when enabled and when the required host-owned beans are present. MCP hosting is off by default; protect `/mcp` with the application's authentication layer. See the [Spring Boot guide](../../src/java/foundgine-providers/SPRING-BOOT.md).

## Redis-compatible shared state

The Redis providers implement shared warrant replay protection and shared MCP session storage for horizontally scaled hosts. They use atomic Redis operations and fail closed when Redis is unavailable. The repository includes `docker-compose.redis.yml` and live-server tests for the .NET provider. Java's Redis replay store uses the optional Lettuce backend.

See also [AI agents and MCP](agents-mcp.md), [Provider catalogue](providers.md) and [Cross-language implementation map](implementation-map.md).
