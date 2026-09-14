# Foundgine Supply Chain (Java) — Starter Sample

The Java counterpart to [`Foundgine.SupplyChain`](../../../csharp/samples/Foundgine.SupplyChain/README.md).

It demonstrates the same application-controlled boundary using the Java packages published under
`io.github.cristianbarragan` (`foundgine-core`, `foundgine-runtime`, `foundgine-providers`) instead of
the .NET NuGet packages:

**Caller → application → semantic model (via `@FoundgineEntity` + AOT annotation processing) → authorization → Foundgine planning/execution → storage**

## What it demonstrates

- A small, framework-neutral Supply Chain domain (`SupplyChainDomain`) expressed as Java `record`s and
  annotated with `@FoundgineEntity` so the `foundgine-aot-generator` annotation processor can emit
  compiled metadata at build time — no runtime reflection, mirroring the C# Roslyn source generator.
- A capability-shaped authorization boundary (`Authorization`) with tenant-scoped, role-based checks
  (`CUSTOMER`, `ANALYST`, `WAREHOUSE_OPERATOR`, `SUPPLY_CHAIN_MANAGER`).
- Deterministic, in-memory equivalents of the adversarial scenarios exercised in the advanced
  proving ground (`Scenarios`): recursive bill-of-materials traversal with cycle detection, and a
  tenant-isolation check for warehouse reads.

## Current state

This module is the Java **domain and authorization port**: the semantic model, authorization
context, and adversarial scenario helpers are in place, and `Main` wires them up as a smoke test.
Provider/database wiring — the equivalent of the C# starter's MCP + PostgreSQL stack
(`SemanticSqlQueryExecutor`, `Program.cs`) — has not been added to this module yet; it is being
built out incrementally as the Java provider contracts stabilize.

If you want to see the **complete** MCP → semantic model → authorization → planning/execution →
PostgreSQL path already wired up in Java today, use
[`../foundgine-supply-chain-advanced`](../foundgine-supply-chain-advanced/README.md) — its
`AdvancedMcpServer`, `AdvancedMutationPipeline`, and `PostgresSupplyChainStore` are the Java
equivalent of what the C# starter's `Program.cs` and `SemanticSqlQueryExecutor` do.

## Where to go next

- [Building the Java Starter, step by step](./SupplyChain-Starter-Tutorial.md) — the same
  file-by-file walk as the C# tutorial, adapted to Maven, annotation processing, and the domain
  classes that already exist in this module.
- [`../foundgine-supply-chain-advanced`](../foundgine-supply-chain-advanced/README.md) — the full
  semantic/authorization/MCP/PostgreSQL proving ground, with its own
  [step-by-step tutorial](../foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md).

## Run

```bash
cd src/java
mvn -pl samples/foundgine-supply-chain -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.Main
```

This compiles the module (running the `foundgine-aot-generator` annotation processor over
`SupplyChainDomain`) and runs `Main`, which prints the configured `FoundgineOptions` as a smoke
test that the Java `foundgine-core` / `foundgine-runtime` packages resolve correctly from this
module.
