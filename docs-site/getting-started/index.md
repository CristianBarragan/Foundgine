# Get started with Foundgine

Run the canonical Supply Chain sample first, in .NET or Java. The website stays focused on the architecture; the repository tutorials contain the exact commands and file-by-file build steps.

## Start here — .NET

- [Starter sample](https://github.com/CristianBarragan/Foundgine/tree/main/src/csharp/samples/Foundgine.SupplyChain)
- [Starter step-by-step tutorial](https://github.com/CristianBarragan/Foundgine/blob/main/src/csharp/samples/Foundgine.SupplyChain/SupplyChain-Starter-Tutorial.md)
- [Starter explained](https://github.com/CristianBarragan/Foundgine/blob/main/src/csharp/samples/Foundgine.SupplyChain/Foundgine-SupplyChain-Explained.md)

## Start here — Java

- [Starter sample](https://github.com/CristianBarragan/Foundgine/tree/main/src/java/samples/foundgine-supply-chain)
- [Starter step-by-step tutorial](https://github.com/CristianBarragan/Foundgine/blob/main/src/java/samples/foundgine-supply-chain/SupplyChain-Starter-Tutorial.md)
- [Starter explained](https://github.com/CristianBarragan/Foundgine/blob/main/src/java/samples/foundgine-supply-chain/Foundgine-SupplyChain-Explained.md)
- [Advanced sample (MCP + PostgreSQL already wired up)](https://github.com/CristianBarragan/Foundgine/tree/main/src/java/samples/foundgine-supply-chain-advanced) and its [step-by-step tutorial](https://github.com/CristianBarragan/Foundgine/blob/main/src/java/samples/foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md)

## Describe your model in code

Both starters configure their semantic model fluently instead of with attributes. Plain domain and storage types stay free of Foundgine references; small configuration classes (semantic, persistence, entity map, connection) are registered once on a `FoundgineModel` subclass, and `Build()` (`build()` in Java) compiles them to the same metadata registry the attribute and AOT-generator path produces. The tutorials above walk through each file. See [Mapping and Connections](https://github.com/CristianBarragan/Foundgine/blob/main/docs/MAPPING.md) for the full API and its current limits.

## Follow the layers

`Caller → Application → Semantic Model → Runtime → Provider → PostgreSQL`

Then move to the [advanced semantic sample](../samples/semantic/index.html) and its numbered repository tutorial (.NET), or the Java advanced tutorial linked above.

## Continue

[Walkthrough](../walkthrough/index.html) → [Architecture](../architecture/index.html)
