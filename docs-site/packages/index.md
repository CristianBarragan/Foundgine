# Foundgine packages

The current package surface is organized as four publishable packages:

| Package | Responsibility |
|---|---|
| `Foundgine.Core` | Semantic model, metadata, intent, planning and provider-independent contracts |
| `Foundgine.Runtime` | Application-facing orchestration, authorization and execution |
| `Foundgine.Providers` | Concrete storage, AI/model, MCP, AOT and other integrations |
| `Foundgine.Extensions` | Optional framework integrations such as Hot Chocolate GraphQL |

A Java port of the same four packages is published to Maven Central under the `io.github.cristianbarragan` namespace:

```xml
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-runtime</artifactId>
  <version>2.2.0</version>
</dependency>
<dependency>
  <groupId>io.github.cristianbarragan</groupId>
  <artifactId>foundgine-providers</artifactId>
  <version>2.2.0</version>
</dependency>
```

`foundgine-core` is brought in transitively by the higher-level artifacts; add it directly when your code targets its contracts. `foundgine-extensions` is available the same way for optional framework integrations.

For installation and package-level details, see the [Packages page](index.html) and the project READMEs under [`src/`](https://github.com/CristianBarragan/Foundgine/tree/main/src).

## Continue

[Samples](../samples/index.html) → [Getting started](../getting-started/index.html)
