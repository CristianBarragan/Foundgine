# LangChain4j integration (foundgine-providers)

LangChain4j tool adapter for Foundgine (Java). It exposes the tools of a `FoundgineMcpServer` to LangChain4j agents, so a LangChain4j `AiServices` assistant gets Foundgine's governed semantic boundary as its data access instead of SQL or credentials.

> Included in the `io.github.cristianbarragan:foundgine-providers` artifact. LangChain4j APIs are optional dependencies; add LangChain4j to your application when using this adapter. Version pins are managed by the Foundgine parent POM.

## What is in the module

`FoundgineLangChain4jTools` (`com.foundgine.langchain4j`) with one public entry point:

```java
Map<ToolSpecification, ToolExecutor> toolMap(FoundgineMcpServer server)
```

Each Foundgine MCP tool becomes a LangChain4j `ToolSpecification` (name, description, string properties and `required` list from the tool's input schema) plus a `ToolExecutor` that calls `server.invoke(...)`.

## Use

```java
FoundgineMcpServer server = FoundgineMcpServer.builder()
        .serverInfo("foundgine", "1.0.0")
        .withReadTools(tools)            // FoundgineMcpTools built with your IFoundgine + security provider
        .build();                        // add .withMutationTools(...) only if you want writes exposed

Assistant assistant = AiServices.builder(Assistant.class)
        .chatModel(model)
        .tools(FoundgineLangChain4jTools.toolMap(server))
        .build();
```

## Security model

- The same boundary as over MCP: tenant and identity come from the host's `ISecurityExecutionContextProvider`, never from the model's arguments.
- Mutation tools exist only if the server was built with them.
- LangChain4j runs tool executors on the calling thread, so thread-bound identity (for example a Spring Security context) is visible to the provider.
- Failures return as text for the model with the same agent-safe messages as MCP. Non-object arguments return `Tool arguments must be a JSON object.`
- Discovery and a successful tool call are not authorization; every call is resolved and authorized by Foundgine.

## Install

```xml
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-providers</artifactId>
  <version>3.4.0</version>
</dependency>
```

The LangChain4j adapter classes are shipped in the same `foundgine-providers` jar as the other Java providers.

## Tests

`FoundgineLangChain4jToolsTest` under `src/java/foundgine-providers/src/test/java/com/foundgine/langchain4j/`. Build from `src/java/` (see [`BUILDING.md`](../BUILDING.md)).

## See also

[Agent and framework integrations](../../../docs/wiki/agent-framework-integrations.md) · [MCP adapter](src/main/java/com/foundgine/providers/tools/mcp/README.md) · [Spring Boot guide](SPRING-BOOT.md) for hosting the MCP endpoint
