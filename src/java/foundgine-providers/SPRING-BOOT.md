# Spring Boot integration (foundgine-providers)

Spring Boot auto-configuration for Foundgine (Java). It wires the Foundgine MCP server, an optional MCP HTTP controller and the JPA ORM provider into a Spring Boot application, **secure by default**: nothing is registered until you opt in and supply the host-owned pieces.

> Included in the `io.github.cristianbarragan:foundgine-providers` artifact. Spring Boot APIs are optional dependencies; add Spring Boot to your application to activate the auto-configuration. Version pins are managed by the Foundgine parent POM.

## What is in the module

| Class | Purpose |
|---|---|
| `FoundgineMcpAutoConfiguration` | Registers `FoundgineMcpTools`, optional `FoundgineMcpMutationTools`, `FoundgineMcpServer`, and (when enabled) the session manager and SSE registry |
| `FoundgineMcpWebAutoConfiguration` | Registers `FoundgineMcpController` when Spring MVC is on the classpath (ordered after the MCP configuration) |
| `FoundgineMcpController` | Streamable-HTTP-style endpoint (default `POST /mcp`); origin checks, optional `Mcp-Session-Id` sessions, optional replayable SSE `GET` |
| `FoundgineJpaAutoConfiguration` | Builds a `FoundgineOrm` from your `FoundgineOrm.Authorization` bean plus any `FoundgineOrmCustomizer` beans |
| `FoundgineOrmCustomizer` | Functional interface to contribute metadata, entities and generated models to the ORM builder |

Registered through `META-INF/spring/org.springframework.boot.autoconfigure.AutoConfiguration.imports`; property metadata is in `additional-spring-configuration-metadata.json`.

## Enable MCP

Requires a Foundgine `IFoundgine` bean and a **host-owned** `ISecurityExecutionContextProvider` that reads the authenticated caller. Foundgine never takes identity from the agent.

```properties
foundgine.mcp.enabled=true                 # off by default
foundgine.mcp.path=/mcp                    # default
foundgine.mcp.allowed-origins=https://app.example   # browser Origin allow-list (DNS-rebinding defence)
foundgine.mcp.mutations-enabled=false      # also needs IFoundgineMutations + FoundgineMcpApprovalTokenPolicy beans
foundgine.mcp.sessions-enabled=false       # Mcp-Session-Id sessions (in-memory store by default; supply your own for clusters)
foundgine.mcp.sse-enabled=false            # replayable SSE stream; needs sessions
```

Further tuning properties: `server-name`, `server-version`, `session-idle-minutes` (30), `session-max-lifetime-minutes` (1440), `max-sessions` (10000), `sse-max-streams`, `sse-max-events` (256), `sse-max-bytes`, `sse-heartbeat-seconds` (25), `sse-max-stream-seconds` (3600), `sse-sweep-seconds` (60), `sse-max-open-streams` (5000) and `sse-max-streams-per-principal` (10).

Protect the endpoint with your own authentication (for example Spring Security) **before** the controller runs; a session never replaces authentication.

## Enable the JPA provider

Define a `FoundgineOrm.Authorization` bean (the build fails closed without one, so authorization is never defaulted) and optionally one or more customizers:

```java
@Bean
FoundgineOrmCustomizer<MyCaller> foundgineModel() {
    return b -> b.metadata(GeneratedFoundgineMetadata.build())
                 .entities(GeneratedOrmMaps.entityTypes())
                 .generatedModels(GeneratedJpaModels.create());
}
```

## Install

```xml
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-providers</artifactId>
  <version>3.4.0</version>
</dependency>
```

The Spring integration classes are shipped in the same `foundgine-providers` jar as the other Java providers.

## Tests

`FoundgineMcpAutoConfigurationTest` and `FoundgineJpaAutoConfigurationTest` under `src/java/foundgine-providers/src/test/java/com/foundgine/spring/`. Build from `src/java/` (see [`BUILDING.md`](../BUILDING.md)).

## See also

[Agent and framework integrations](../../../docs/wiki/agent-framework-integrations.md) · [MCP adapter](src/main/java/com/foundgine/providers/tools/mcp/README.md) · [Extensions](../../../docs/wiki/extensions.md)
