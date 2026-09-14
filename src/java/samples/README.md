# Foundgine Java samples

The Java sample port mirrors the C# `src/csharp/samples` learning path: a small **starter** sample,
then a fully wired **advanced** proving ground.

| Sample | Purpose | Start with |
|---|---|---|
| [`foundgine-supply-chain`](./foundgine-supply-chain/README.md) | Domain, semantic annotations, and authorization shape — the smallest path to a compiling Foundgine Java application. | [Starter tutorial](./foundgine-supply-chain/SupplyChain-Starter-Tutorial.md) |
| [`foundgine-supply-chain-advanced`](./foundgine-supply-chain-advanced/README.md) | Full semantic proving ground: claims/authorization, high-assurance mutation, MCP transport, PostgreSQL, and adversarial security scenarios. | [Advanced tutorial](./foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md) |

The domain mirrors the canonical C# sample entities and is intentionally framework-neutral;
provider/database wiring for the starter is added incrementally as the Java provider contracts
stabilize — the advanced module already has the complete MCP + PostgreSQL path today.

See each sample's own README and tutorial for details, and
[`../../csharp/samples/README.md`](../../csharp/samples/README.md) for the C# reference these keep
parity with.
