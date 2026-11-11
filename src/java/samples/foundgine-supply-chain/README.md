# Foundgine Supply Chain (Java) — Starter Sample

The Java counterpart to [`Foundgine.SupplyChain`](../../../csharp/samples/Foundgine.SupplyChain/README.md).

It demonstrates the same application-controlled boundary using the Java packages published under
`io.github.cristianbarragan` (`foundgine-core`, `foundgine-runtime`, `foundgine-providers`) instead of
the .NET NuGet packages:

**Caller → application → semantic model (via `@FoundgineEntity` + AOT annotation processing) → authorization → Foundgine planning/execution → storage**

## What it demonstrates

- A small, framework-neutral Supply Chain domain (`SupplyChainDomain`) expressed as Java `record`s and
  annotated with `@FoundgineEntity` so the AOT annotation processor embedded in the `foundgine-providers` JAR can emit
  compiled metadata at build time — no runtime reflection, mirroring the C# Roslyn source generator.
- A capability-shaped authorization boundary (`Authorization`) with tenant-scoped, role-based checks
  (`CUSTOMER`, `ANALYST`, `WAREHOUSE_OPERATOR`, `SUPPLY_CHAIN_MANAGER`).
- Deterministic, in-memory equivalents of the adversarial scenarios exercised in the advanced
  proving ground (`Scenarios`): recursive bill-of-materials traversal with cycle detection, and a
  tenant-isolation check for warehouse reads.

## Current state

This module is the Java **domain, authorization and persistence port**: the semantic model,
authorization context, adversarial scenario helpers, and a **JPA + PostgreSQL read path**
(`jpa/SupplyChainJpa`, `SupplyChainSemantics`, `SupplyChainPolicy`) are in place, and `Main` runs them as a smoke test.

The persistence path is: semantic read → `SemanticAuthorizer` with a real tenant/role policy
(`SupplyChainPolicy`: row-level tenant predicate, field-level `riskScore`, customer entity limits) →
`Planner` →
`ExecutionIR` → `JpaOrmProvider` plan → JPA Criteria execution through Hibernate → PostgreSQL.
Three entities are mapped to JPA classes (`Supplier`, `Warehouse`, `Product`); the rest of the
domain stays semantic-only (the semantic contract is scoped to the mapped entities, because
`ProductComponent` has a composite key and cannot become a contract entity). The MCP transport and the mutation path are **not** wired in this
starter yet.

> **Status.** The Foundgine half (metadata → authorization → plan → `ExecutionIR` → ORM plan →
> execution) is covered by `SupplyChainSemanticsTest` and was run against the in-memory ORM source
> on JDK 21. The Hibernate/PostgreSQL half (`SupplyChainJpa`, `persistence.xml`, the SQL files) has
> **not** been compiled or run: no JPA 3.2 jars or network were available when it was written.

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
cd src/java/samples/foundgine-supply-chain
docker compose up -d          # PostgreSQL on localhost:4429, schema + seed applied on first start
cd ../..
mvn -pl samples/foundgine-supply-chain -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.Main
```

Run the in-memory test (no database needed):

```bash
cd src/java
mvn -pl samples/foundgine-supply-chain -am test -Dtest=SupplyChainSemanticsTest -Dsurefire.failIfNoSpecifiedTests=false
```

Connection settings come from `SUPPLYCHAIN_JDBC_URL`, `SUPPLYCHAIN_JDBC_USER` and
`SUPPLYCHAIN_JDBC_PASSWORD` (defaults match `docker-compose.yml`). If PostgreSQL is not reachable,
`Main` prints a "JPA demo skipped" line instead of failing.

This compiles the module (running the AOT annotation processor embedded in the `foundgine-providers` JAR over
`SupplyChainDomain`) and runs `Main`, which prints the configured `FoundgineOptions` and then lists the
`tenant-a` US suppliers an `ANALYST` can read through JPA.
