# Agent and framework integrations: Microsoft Agent Framework, LangChain4j and Spring Boot

Three optional integrations connect Foundgine to the frameworks people already build agents and services with. They are separate from the core packages, and each one changes the *transport or hosting*, never the authority model.

> **In plain words.** The agent framework (MAF or LangChain4j) handles the conversation. Foundgine handles what the agent is allowed to do. The framework sees Foundgine as a small set of tools; Foundgine resolves, authorizes and executes every call, and the host, not the model, supplies who the caller is. Spring Boot hosts the MCP endpoint and the JPA provider with auto-configuration.

| Integration | Language | Module | What it gives you | Docs |
|---|---|---|---|---|
| **Microsoft Agent Framework (MAF)** | C# / .NET | `Foundgine.Providers.AgentFramework` (`src/csharp/Foundgine.Providers.AgentFramework`) | Foundgine tools as Microsoft.Extensions.AI `AITool`s, a MAF `ChatClientAgent` factory, DI registration, human-approved writes | [README](../../src/csharp/Foundgine.Providers.AgentFramework/README.md) |
| **LangChain4j** | Java | `foundgine-langchain4j` (`src/java/foundgine-langchain4j`) | `FoundgineLangChain4jTools.toolMap(server)` turns Foundgine's MCP tools into LangChain4j tool specifications and executors for `AiServices` | [README](../../src/java/foundgine-langchain4j/README.md) |
| **Spring Boot** | Java | `foundgine-spring` (`src/java/foundgine-spring`) | Auto-configuration for the Foundgine MCP server and HTTP controller (sessions, SSE) and for the JPA ORM provider | [README](../../src/java/foundgine-spring/README.md) |

## Verification status (read this before depending on one)

| Integration | Source | Tests | Status in this snapshot |
|---|---|---|---|
| MAF adapter | Present | `src/csharp/tests/Foundgine.AgentFramework.Tests` | Separate packable project, **not in `Foundgine.sln`**, not build-verified, not released. It post-dates the `3.3.5` tag. |
| `foundgine-langchain4j` | Present | `FoundgineLangChain4jToolsTest` | In the Maven reactor and publishing configuration. Compiled and tested against LangChain4j 1.0.0 sources with `javac 21`, not Maven; dependency pin and runtime verification pending. |
| `foundgine-spring` | Present | `FoundgineMcpAutoConfigurationTest`, `FoundgineJpaAutoConfigurationTest` | In the Maven reactor and publishing configuration. Written without the Spring Boot jars; dependency pins (Spring Boot 4.0.0) and runtime verification pending. |

"Source and tests present" is not "verified". Check the module README, the source and your own build before relying on any of them, and confirm a published artifact exists before pinning a version.

## Shared security model

All three expose the same boundary the MCP adapter does:

- **Identity is host-owned.** Tools resolve the host's security execution context at call time (`ISecurityExecutionContextProvider`) and fail closed when it is missing. The model cannot supply tenant, subject, audience, warrant or authorization context.
- **Discovery is not authorization.** `foundgine_capabilities` describes the surface; every call is re-resolved and re-authorized.
- **Writes are opt-in and governed.** MAF mutations are off by default and executed only after MAF human approval, with no model-callable approve tool. In Java, mutation tools exist only if the server was built with them; Spring additionally needs `foundgine.mcp.mutations-enabled=true`, an `IFoundgineMutations` bean and an approval-token policy.
- **Failures are agent-safe.** Errors come back as text with the same safe messages as MCP.

## Microsoft Agent Framework (C#)

```csharp
builder.Services.AddFoundgine(/* semantic model, providers */);
builder.Services.AddSingleton<ISecurityExecutionContextProvider, HttpContextSecurityProvider>(); // host-owned
builder.Services.AddSingleton<IChatClient>(/* any Microsoft.Extensions.AI chat client */);
builder.Services.AddFoundgineAgentFramework(o => { o.AgentName = "orders"; o.EnableMutations = true; });

var agent = scope.ServiceProvider.GetRequiredKeyedService<AIAgent>("orders");
var response = await agent.RunAsync("Which purchase orders are overdue?", await agent.CreateSessionAsync());
```

Tools: `foundgine_capabilities`, `foundgine_query_semantic`, `foundgine_query` (only with lexical grounding registered), `foundgine_mutation_dry_run`, and `foundgine_mutation_execute` (wrapped for MAF human approval, plan-fingerprint re-validated). It also declares a `foundgine.maf.agent` provider for gateway discovery. Without DI use `FoundgineAgentFactory.Create(chatClient, tools, options)`. Install: `dotnet add package Foundgine.Providers.AgentFramework` once a release exists.

## LangChain4j (Java)

```java
var assistant = AiServices.builder(Assistant.class)
        .chatModel(model)
        .tools(FoundgineLangChain4jTools.toolMap(server)) // server: FoundgineMcpServer
        .build();
```

Maven: `io.github.cristianbarragan:foundgine-langchain4j:3.3.5` (verify availability). Tool executors run on the calling thread, so thread-bound identity such as a Spring Security context is visible to the host's security provider.

## Spring Boot (Java)

```properties
foundgine.mcp.enabled=true
foundgine.mcp.allowed-origins=https://app.example
# optional: foundgine.mcp.mutations-enabled, sessions-enabled, sse-enabled, path (default /mcp)
```

Needs an `IFoundgine` bean and an `ISecurityExecutionContextProvider`; protect `/mcp` with your own authentication. For JPA, define a `FoundgineOrm.Authorization` bean (no default, fails closed) and optional `FoundgineOrmCustomizer` beans. Full property list and examples are in the [module README](../../src/java/foundgine-spring/README.md).

## How they fit with the other pieces

- Hosting an MCP server for any agent client: [AI agents and MCP](agents-mcp.md), MCP adapter READMEs, and the Spring module above.
- Calling a server from outside .NET or Java: [Client SDKs](client-sdks.md) (TypeScript, Python, WebAssembly).
- Microsoft.Extensions.AI function-calling without MAF: [AI agents](../AI-AGENT.md) and `Foundgine.Providers.Models`.
- Where each lives in C# and Java: [Cross-language implementation map](implementation-map.md).
