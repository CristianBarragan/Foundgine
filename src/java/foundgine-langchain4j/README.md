# foundgine-langchain4j

LangChain4j tool adapter for Foundgine (Java). It exposes the tools of a `FoundgineMcpServer` to LangChain4j agents, so a LangChain4j `AiServices` assistant gets Foundgine's governed semantic boundary as its data access instead of SQL or credentials.

> **Status: source and tests present; dependency pin and runtime verification pending.** The class comment records that it was compiled and tested against the LangChain4j 1.0.0 sources with `javac 21`, not Maven. `pom.xml` pins `langchain4j` and `langchain4j-core` `1.0.0`.

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
  <artifactId>foundgine-langchain4j</artifactId>
  <version>3.4.0</version>
</dependency>
```

Verify the artifact exists in Maven Central before pinning.

## Tests

`FoundgineLangChain4jToolsTest` under `src/test/java/com/foundgine/langchain4j/`. Build from `src/java/` (see [`BUILDING.md`](../BUILDING.md)).

## See also

[Agent and framework integrations](../../../docs/wiki/agent-framework-integrations.md) · [MCP adapter](../foundgine-providers/src/main/java/com/foundgine/providers/tools/mcp/README.md) · [`foundgine-spring`](../foundgine-spring/README.md) for hosting the MCP endpoint
