# Get started with Foundgine

Run the canonical Supply Chain sample first, in .NET or Java. The current sample READMEs are the source of truth for setup, commands, configuration and implementation details.

## Start here — .NET

- [Starter sample](https://github.com/Foundgine/Foundgine/tree/main/src/csharp/samples/Foundgine.SupplyChain)

## Start here — Java

- [Starter sample](https://github.com/Foundgine/Foundgine/tree/main/src/java/samples/foundgine-supply-chain)
- [Advanced sample (MCP + PostgreSQL source path)](https://github.com/Foundgine/Foundgine/tree/main/src/java/samples/foundgine-supply-chain-advanced) and its [step-by-step tutorial](https://github.com/Foundgine/Foundgine/blob/main/src/java/samples/foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md)

  **Verification note:** the latest reported Maven run failed in the Advanced sample test phase. Fixes are present but have not yet been rerun; see the sample README before treating the Java Advanced path as verified.

## Describe your model in code

Both starters configure their semantic model fluently instead of with attributes. Plain domain and storage types stay free of Foundgine references; small configuration classes (semantic, persistence, entity map, connection) are registered once on a `FoundgineModel` subclass, and `Build()` (`build()` in Java) compiles them to the same metadata registry the attribute and AOT-generator path produces. The sample READMEs describe the current implementation. See [Mapping and Connections](https://github.com/Foundgine/Foundgine/blob/main/docs/MAPPING.md) for the full API and its current limits.

## Follow the layers

`Caller → Application → Semantic Model → Runtime → Provider → PostgreSQL`

Then move to the [advanced semantic sample](../samples/semantic/index.html) and its numbered repository tutorial (.NET), or the Java advanced tutorial linked above.

## Continue

[Walkthrough](../walkthrough/index.html) → [Architecture](../architecture/index.html)
