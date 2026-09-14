# Building the Foundgine Supply Chain "Starter" Sample (Java) — Step by Step

This is the Java counterpart to
[`SupplyChain-Starter-Tutorial.md`](../../../csharp/samples/Foundgine.SupplyChain/SupplyChain-Starter-Tutorial.md).
It walks you through building `src/java/samples/foundgine-supply-chain` from an empty Maven
module, piece by piece, so you understand _why_ every file exists — not just how to run it. By the
end you'll have a small Maven module whose domain, storage-facing metadata, and authorization are
generated and checked at compile time by the same kind of AOT annotation processor the C# sample
uses at build time, just implemented as a Java `javax.annotation.processing.Processor` instead of a
Roslyn source generator.

> Reference implementation: `src/java/samples/foundgine-supply-chain` in this repo. Everything
> below matches that module, so you can always cross-check against the real files if you get stuck.
>
> See also: [`Foundgine-SupplyChain-Explained.md`](./Foundgine-SupplyChain-Explained.md) in this
> same folder, which walks through _why_ each concept below exists.

---

## 0. What you're building

```
AI agent / MCP client
        │
        ▼
  Application capability
        │
        ▼
  Foundgine semantic model (generated from @FoundgineEntity)
        │
        ▼
  Authorization (tenant + role scoped)
        │
        ▼
  Foundgine planning/execution → storage
```

The starter keeps the Java port small on purpose: it proves out the domain, the annotation-driven
metadata generation, and the authorization shape first. The full MCP transport and PostgreSQL
execution path already exist in Java — see
[`../foundgine-supply-chain-advanced`](../foundgine-supply-chain-advanced/README.md) — and this
starter is where that same wiring will land for the minimal case as the Java provider surface
stabilizes.

---

## 1. Prerequisites

- **JDK 21** (`maven.compiler.release` for this reactor)
- **Maven 3.9+**
- **Docker with Compose support** (only needed once you add the PostgreSQL provider — see
  the [Advanced tutorial](../foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md))
- A clone of this repository — the module uses `${revision}` project references into
  `foundgine-core`, `foundgine-runtime`, `foundgine-providers`, and `foundgine-aot-generator`
  from the same Maven reactor, exactly like the real sample

Verify:

```bash
java -version    # should print 21 or newer
mvn -version
```

---

## 2. Create the module and add the Foundgine artifacts

Foundgine's Java port publishes the same four architectural boundaries as the .NET packages, under
the `io.github.cristianbarragan` groupId:

| Artifact               | Role                                                         |
| ---------------------- | ------------------------------------------------------------ |
| `foundgine-core`       | Contracts, semantic model, metadata, planning, serialization |
| `foundgine-runtime`    | Orchestration, execution, application-facing APIs            |
| `foundgine-providers`  | Storage (PostgreSQL), MCP, and AOT provider implementations  |
| `foundgine-extensions` | Optional caller-facing adapters                              |

For a normal application the minimum footprint is `foundgine-runtime` + `foundgine-providers` —
`foundgine-core` comes along transitively. Create the module skeleton next to the other Java
modules:

```bash
mkdir -p src/java/samples/foundgine-supply-chain/src/main/java/com/foundgine/samples/supplychain
cd src/java/samples/foundgine-supply-chain
```

Add a `pom.xml` that joins the reactor as a child of `src/java/pom.xml`, exactly like the real
module:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<project
  xmlns="http://maven.apache.org/POM/4.0.0"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 http://maven.apache.org/xsd/maven-4.0.0.xsd"
>
  <modelVersion>4.0.0</modelVersion>

  <parent>
    <groupId>io.github.cristianbarragan</groupId>
    <artifactId>foundgine-parent</artifactId>
    <version>${revision}</version>
    <relativePath>../../pom.xml</relativePath>
  </parent>

  <artifactId>foundgine-sample-supply-chain</artifactId>

  <dependencies>
    <dependency>
      <groupId>io.github.cristianbarragan</groupId>
      <artifactId>foundgine-core</artifactId>
      <version>${revision}</version>
    </dependency>
    <dependency>
      <groupId>io.github.cristianbarragan</groupId>
      <artifactId>foundgine-runtime</artifactId>
      <version>${revision}</version>
    </dependency>
    <dependency>
      <groupId>io.github.cristianbarragan</groupId>
      <artifactId>foundgine-providers</artifactId>
      <version>${revision}</version>
    </dependency>
    <dependency>
      <groupId>org.postgresql</groupId>
      <artifactId>postgresql</artifactId>
    </dependency>
    <dependency>
      <groupId>org.junit.jupiter</groupId>
      <artifactId>junit-jupiter</artifactId>
      <scope>test</scope>
    </dependency>
  </dependencies>

  <build>
    <plugins>
      <plugin>
        <groupId>org.apache.maven.plugins</groupId>
        <artifactId>maven-compiler-plugin</artifactId>
        <configuration>
          <compilerArgs>
            <arg>-Afoundgine.generated.package=com.foundgine.samples.supplychain.generated</arg>
            <arg>-Afoundgine.generated.name=GeneratedFoundgineMetadata</arg>
          </compilerArgs>
          <annotationProcessorPaths>
            <path>
              <groupId>io.github.cristianbarragan</groupId>
              <artifactId>foundgine-aot-generator</artifactId>
              <version>${revision}</version>
            </path>
          </annotationProcessorPaths>
        </configuration>
      </plugin>
    </plugins>
  </build>
</project>
```

> **Why a compile-time annotation processor?** Just like the C# Roslyn generator, Foundgine needs
> to know your entities, fields, and relationships _before_ it can plan a query. The
> `foundgine-aot-generator` annotation processor reads `@FoundgineEntity` (and friends) at compile
> time and emits a generated metadata class you use directly in code — no runtime reflection.

Register the module in `src/java/pom.xml`'s `<modules>` list if it isn't already there, and in
`src/java/samples/README.md`.

---

## 3. Define the domain

This is the model your application talks about, decorated with `@FoundgineEntity` so the
annotation processor picks it up. This is exactly `SupplyChainDomain.java` in the real module —
create it under
`src/main/java/com/foundgine/samples/supplychain/SupplyChainDomain.java`:

```java
package com.foundgine.samples.supplychain;

import com.foundgine.providers.aot.*;
import java.math.BigDecimal;
import java.time.*;
import java.util.List;

/** Complete provider-neutral Supply Chain sample domain. */
public final class SupplyChainDomain {
    private SupplyChainDomain() {}

    public record CompanyId(int value) {}
    public record WarehouseId(int value) {}
    public record ProductId(int value) {}
    // ... one small identifier record per entity, matching the real file.

    @FoundgineEntity(name = "Company")
    public record Company(int id, String name, @FoundgineSemanticDimension("tenant") String tenantId) {}

    @FoundgineEntity(name = "Warehouse")
    public record Warehouse(int id, @FoundgineSemanticDimension("businessUnit") int businessUnitId,
                             String name, @FoundgineSemanticDimension("tenant") String tenantId) {}

    @FoundgineEntity(name = "Supplier")
    @FoundgineAlias(value = "Vendor", weight = 95)
    @FoundgineAlias(value = "Seller", weight = 90)
    public record Supplier(int id, String name, @FoundgineSemanticDimension("country") String country,
                            BigDecimal riskScore, @FoundgineSemanticDimension("tenant") String tenantId) {}

    @FoundgineEntity(name = "Product")
    public record Product(int id, String sku, String name,
                           @FoundgineSemanticDimension("category") String category, BigDecimal safetyStock) {}

    // Purchase orders, shipments, inventory lots, customers, and customer
    // orders follow the same record + @FoundgineEntity shape — see the real
    // file for the full set.
}
```

**What matters here:**

- `@FoundgineEntity(name = "...")` registers a Java `record` as a semantic entity under a stable
  name — the annotation-processor equivalent of C#'s `[FoundgineModel]`/`[FoundgineEntity]`.
- `@FoundgineSemanticDimension("...")` tags a component as a semantically meaningful dimension
  (tenant, country, category, business unit) that authorization and grounding can reason about —
  this has no direct C# equivalent in the basic starter and is used more heavily in the advanced
  sample's tenant isolation checks.
- `@FoundgineAlias(value = "...", weight = N)` declares application-provided vocabulary evidence
  for a name (e.g. "Vendor" as an alias for "Supplier") — the Java equivalent of the weighted alias
  evidence feature documented in the C# advanced sample.
- Records, not mutable classes, keep the domain immutable by construction — idiomatic Java for a
  model that Foundgine treats as read-only structural metadata.

---

## 4. Authorization

This is the security boundary. Create
`src/main/java/com/foundgine/samples/supplychain/Authorization.java`:

```java
package com.foundgine.samples.supplychain;

import java.util.*;

public final class Authorization {
  public enum Role { CUSTOMER, ANALYST, WAREHOUSE_OPERATOR, SUPPLY_CHAIN_MANAGER }

  public record Context(String tenantId, Set<Integer> allowedWarehouses, Role role, boolean readOnly) {
    public Context {
      Objects.requireNonNull(tenantId);
      allowedWarehouses = Set.copyOf(allowedWarehouses);
      Objects.requireNonNull(role);
    }
  }

  public static boolean canReadSupplierRisk(Context c) {
    return c.role() == Role.ANALYST || c.role() == Role.SUPPLY_CHAIN_MANAGER;
  }

  public static boolean canWrite(Context c) {
    return !c.readOnly() && (c.role() == Role.WAREHOUSE_OPERATOR || c.role() == Role.SUPPLY_CHAIN_MANAGER);
  }

  public static boolean canReadWarehouse(Context c, int warehouseId) {
    return c.allowedWarehouses().contains(warehouseId);
  }

  private Authorization() {}
}
```

**Why this shape:** the C# starter's `SupplyChainAuthorizer` keys off an `actor` string and a
per-actor allow-list of named capabilities (`get_my_orders`, `place_order`, ...). The Java starter
instead models authorization as an immutable `Context` record — tenant, allowed warehouses, role,
and a read-only flag — checked by small static predicates. Both approaches enforce the same
principle: **authorization runs before any query is built**, and a denied request never reaches the
planner. Pick whichever shape fits your application; the advanced sample
(`SupplyChainAuthorization`) shows how to wire either shape into Foundgine's semantic authorization
policy (`ConfiguredSemanticAuthorizationPolicy`) so the check is enforced by the planner itself, not
just by application code that might forget to call it.

---

## 5. Adversarial scenarios as a smoke test

The starter also ports the deterministic, in-memory equivalents of the high-value adversarial
scenarios the advanced C# sample exercises with full authorization and a live database. Create
`src/main/java/com/foundgine/samples/supplychain/Scenarios.java`:

```java
package com.foundgine.samples.supplychain;

import java.util.*;

/** Deterministic, in-memory equivalents of the high-value adversarial scenarios. */
public final class Scenarios {
  public record SupplierExposure(int productId, int supplierId, int depth, boolean cycleDetected) {}

  public static List<SupplierExposure> recursiveSupplierRisk(Map<Integer, List<Integer>> bom, int root, int maxDepth) {
    List<SupplierExposure> out = new ArrayList<>();
    Set<Integer> visited = new HashSet<>();
    walk(bom, root, 0, maxDepth, new HashSet<>(), visited, out);
    return List.copyOf(out);
  }

  private static void walk(Map<Integer, List<Integer>> bom, int p, int depth, int max,
                            Set<Integer> path, Set<Integer> visited, List<SupplierExposure> out) {
    if (depth > max) return;
    if (!path.add(p)) { out.add(new SupplierExposure(p, 0, depth, true)); return; }
    if (!visited.add(p)) { path.remove(p); return; }
    for (int child : bom.getOrDefault(p, List.of())) walk(bom, child, depth + 1, max, path, visited, out);
    path.remove(p);
  }

  public static void assertAdversarialInvariants() {
    Map<Integer, List<Integer>> bom = Map.of(1, List.of(2), 2, List.of(3), 3, List.of(1));
    if (recursiveSupplierRisk(bom, 1, 16).stream().noneMatch(SupplierExposure::cycleDetected))
      throw new AssertionError("BOM cycle not detected");
    var auth = new Authorization.Context("tenant-a", Set.of(1, 2), Authorization.Role.ANALYST, false);
    if (Authorization.canReadWarehouse(auth, 3))
      throw new AssertionError("tenant isolation violated");
  }

  private Scenarios() {}
}
```

**What matters here:** a bill-of-materials graph can legitimately contain cycles in adversarial or
malformed input (`Product 1 → 2 → 3 → 1`), and a naive recursive traversal over it would loop
forever. `recursiveSupplierRisk` tracks the current path (`path`) as well as everything ever visited
(`visited`) so a repeated node on the _current_ path is reported as a detected cycle instead of
causing unbounded recursion — the same invariant the advanced sample's
`ScenariosAdversarialInvariants` and `RecursiveSupplierRiskParityTest` verify end to end against a
live semantic model and PostgreSQL.

**Checkpoint — build now:**

```bash
cd src/java
mvn -pl samples/foundgine-supply-chain -am compile
```

If this succeeds, the `foundgine-aot-generator` annotation processor has produced a generated
metadata class in the background containing your `SupplyChainDomain` entities as compiled metadata
— you never write or see this file by hand, exactly like the C# generator's
`GeneratedMetadata`/`GeneratedSemanticModel`.

---

## 6. Wire it together in `Main`

The starter's `Main` is intentionally a small smoke test rather than a full server — it proves the
module compiles against `foundgine-runtime` and that the annotation-processed metadata is on the
classpath:

```java
package com.foundgine.samples.supplychain;

import com.foundgine.runtime.*;

public final class Main {
  public static void main(String[] args) {
    FoundgineOptions options = new FoundgineOptions();
    System.out.println("Foundgine Supply Chain Java sample");
    System.out.println("Runtime configured: " + options);
  }
}
```

Run it:

```bash
cd src/java
mvn -pl samples/foundgine-supply-chain -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.Main
```

---

## 7. Where to go next

You now have the Java domain, authorization shape, and adversarial-scenario smoke test in place —
the same starting point the C# starter reaches by step 7 of its own tutorial, before it adds the
MCP transport and the PostgreSQL-backed query/mutation repositories.

For the **complete** MCP → semantic model → authorization → planning/execution → PostgreSQL path,
already implemented in Java, continue with
[`../foundgine-supply-chain-advanced`](../foundgine-supply-chain-advanced/README.md) and its
[step-by-step tutorial](../foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md), which
walks through `AdvancedMcpServer`, `AdvancedMutationPipeline`, and `PostgresSupplyChainStore` —
the Java equivalents of the C# starter's `Program.cs` and `SemanticSqlQueryExecutor`, plus the
high-assurance mutation, grounding, and adversarial-security coverage from the C# advanced sample.
