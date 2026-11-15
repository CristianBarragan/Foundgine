# foundgine-providers

The Java provider artifact contains the concrete Foundgine infrastructure adapters and framework integrations.

Included integrations:

- Redis-compatible shared warrant replay protection and shared MCP session storage (`LettuceReplayBackend` / `LettuceScriptBackend`, optional Lettuce dependency).
- Spring Boot auto-configuration for MCP hosting and JPA (`com.foundgine.spring`).
- LangChain4j tool integration (`com.foundgine.langchain4j`).
- SQL, PostgreSQL, JPA, MCP, event, gateway, retrieval and AOT provider implementations.

Spring Boot and LangChain4j framework APIs are optional Maven dependencies. Add the corresponding framework to the application when using those adapters. Lettuce is optional and is needed only when constructing the Lettuce-backed Redis implementation.

```xml
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-providers</artifactId>
  <version>3.3.5</version>
</dependency>
```

Guides: [Spring Boot](SPRING-BOOT.md) · [LangChain4j](LANGCHAIN4J.md).


Build and test from `src/java/` with the Java version required by the root POM:

```bash
mvn -f pom.xml clean package
```
