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
  Foundgine planning/execution → JPA (Hibernate) → PostgreSQL
```

The starter keeps the Java port small on purpose: it proves out the domain, the annotation-driven
metadata generation, the authorization shape, and one **JPA + PostgreSQL read path**. The MCP
transport and the mutation path already exist in Java — see
[`../foundgine-supply-chain-advanced`](../foundgine-supply-chain-advanced/README.md) — and are not
part of this starter yet.

> **Status.** The Foundgine half (metadata → authorization → plan → `ExecutionIR` → ORM plan →
> execution) is covered by `SupplyChainSemanticsTest` and was run against the in-memory ORM source
> on JDK 21. The Hibernate/PostgreSQL half (`SupplyChainOrm`, `SupplyChainJpa`, `persistence.xml`, the SQL files) has
> **not** been compiled or run: no JPA 3.2 jars or network were available when it was written.

---

## 1. Prerequisites

- **JDK 27** (`maven.compiler.release` for this reactor)
- **Maven 3.9+**
- **Docker with Compose support** (runs the PostgreSQL used by the JPA read path in section 7)
- A clone of this repository — the module uses `${revision}` project references into
  `foundgine-core`, `foundgine-runtime`, `foundgine-providers`, and `foundgine-providers`
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
| `foundgine-providers`  | Storage (ORM/JPA, PostgreSQL), MCP, and AOT provider implementations |
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
    <!-- JPA + Hibernate ORM: the persistence layer behind Foundgine's storage.orm.jpa provider. -->
    <dependency>
      <groupId>jakarta.persistence</groupId>
      <artifactId>jakarta.persistence-api</artifactId>
    </dependency>
    <dependency>
      <groupId>org.hibernate.orm</groupId>
      <artifactId>hibernate-core</artifactId>
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
              <artifactId>foundgine-providers</artifactId>
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
> AOT annotation processor embedded in the `foundgine-providers` JAR reads `@FoundgineEntity` (and friends) at compile
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

> **Prefer code over annotations?** Since 2.3.0 there is a second way to produce the same
> metadata: the fluent configuration API in `com.foundgine.core.semantic.configuration`. The
> records stay plain (no `@Foundgine*` imports) and small configuration classes declare the entity
> name, dimensions, tables, keys and joins. For example, `Company` above becomes:
>
> ```java
> static final class CompanySemantics extends FoundgineModelConfiguration<Company> {
>     @Override
>     public void configure(FoundgineModelBuilder<Company> model) {
>         model.toEntity("Company");
>         model.hasDimension(Company::tenantId).as("tenant");
>     }
> }
> ```
>
> registered on a `FoundgineModel` subclass and compiled with `build()`. This starter stays on
> annotations so you can see the annotation processor at work; the
> [advanced sample](../foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md) (section 1)
> is built entirely with the fluent API. Both feed the same planner and providers.

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

If this succeeds, the AOT annotation processor embedded in the `foundgine-providers` JAR has produced a generated
metadata class in the background containing your `SupplyChainDomain` entities as compiled metadata
— you never write or see this file by hand, exactly like the C# generator's
`GeneratedMetadata`/`GeneratedSemanticModel`.

---

## 6. Persistence: JPA entities, schema and compose file

The data layer is **JPA (Hibernate) + PostgreSQL**, reached through Foundgine's
`foundgine-providers` ORM provider (`storage.orm.jpa`). The semantic layers above it never see a
table or column name; only the JPA entity classes do.

The domain types are immutable `record`s, which JPA cannot hydrate, so each persisted entity gets a
small mutable JPA class in `jpa/`. **Attribute names equal the semantic field names** (`id`,
`name`, `riskScore`, ...), which is how the ORM registry binds a semantic field to a JPA attribute
without any extra mapping. Columns are declared with `@Column`:

```java
package com.foundgine.samples.supplychain.jpa;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

import java.math.BigDecimal;

/** JPA entity behind the semantic {@code Supplier}. Attribute names equal the semantic field names. */
@Entity
@Table(name = "suppliers")
public class SupplierEntity {
    @Id @Column(name = "supplier_id") private Integer id;
    @Column(name = "supplier_name") private String name;
    @Column(name = "country") private String country;
    @Column(name = "risk_score") private BigDecimal riskScore;
    @Column(name = "tenant_id") private String tenantId;

    protected SupplierEntity() {}

    public SupplierEntity(Integer id, String name, String country, BigDecimal riskScore, String tenantId) {
        this.id = id; this.name = name; this.country = country; this.riskScore = riskScore; this.tenantId = tenantId;
    }

    public Integer getId() { return id; }
    public void setId(Integer id) { this.id = id; }
    public String getName() { return name; }
    public void setName(String name) { this.name = name; }
    public String getCountry() { return country; }
    public void setCountry(String country) { this.country = country; }
    public BigDecimal getRiskScore() { return riskScore; }
    public void setRiskScore(BigDecimal riskScore) { this.riskScore = riskScore; }
    public String getTenantId() { return tenantId; }
    public void setTenantId(String tenantId) { this.tenantId = tenantId; }
}
```

`WarehouseEntity` and `ProductEntity` follow the same shape (copy them from
`src/main/java/com/foundgine/samples/supplychain/jpa/`). They are listed in
`src/main/resources/META-INF/persistence.xml`, which sets `hibernate.hbm2ddl.auto=none`: the schema
belongs to `src/main/resources/database/schema.sql`, never to Hibernate. `seed.sql` inserts two
tenants' worth of rows so tenant filtering is visible, and `docker-compose.yml` starts PostgreSQL
on port 4429 and applies both files on first start:

```bash
cd src/java/samples/foundgine-supply-chain
docker compose up -d
```

> **Scope.** Only `Supplier`, `Warehouse` and `Product` are mapped. Relationship traversal needs
> both ends mapped, so extend `ENTITY_TYPES` (and the schema) before querying across entities.

---

## 7. The JPA query path

The read path is split in three so the Foundgine half can be tested without a database.

### 7a. The policy

`jpa/SupplyChainPolicy.java` turns the caller's `Authorization.Context` (section 4) into a real
Foundgine authorization policy. It enforces row-level tenant isolation and role-based access instead of allowing everything:

```java
package com.foundgine.samples.supplychain.jpa;

import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.semantic.authorization.ConfiguredSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationConfiguration;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationContext;
import com.foundgine.samples.supplychain.Authorization;

import java.util.Map;

/**
 * The starter's real authorization policy, built from the caller's {@link Authorization.Context}.
 *
 * <ul>
 *   <li><b>Entity access:</b> a {@code CUSTOMER} may read only {@code Product}; every other role may read the
 *       three mapped entities.
 *   <li><b>Field access:</b> {@code Supplier.riskScore} needs {@link Authorization#canReadSupplierRisk}.
 *       Unauthorized fields are dropped from the plan rather than failing the request.
 *   <li><b>Row-level tenant isolation:</b> reads of {@code Supplier} and {@code Warehouse} carry the predicate
 *       {@code resource.tenantId == context.tenantId}, which the ORM provider ANDs onto the query. A caller
 *       filter on another tenant therefore returns no rows instead of that tenant's data.
 *   <li><b>Writes and relationships:</b> denied. The starter has no mutation path and maps no relationships.
 * </ul>
 *
 * <p>The tenant value is supplied at execution time through {@link #executionContext}; a missing value fails
 * closed. Warehouse-level scoping ({@code allowedWarehouses}) is not enforced here.
 */
public final class SupplyChainPolicy {
    /** Resource member (JPA attribute) and context path used by the tenant predicate. */
    static final String TENANT_ATTRIBUTE = "tenantId";
    static final String TENANT_CONTEXT_KEY = "context.tenantId";

    private SupplyChainPolicy() {}

    public static ConfiguredSemanticAuthorizationPolicy create(
            SupplyChainSemantics semantics, Authorization.Context caller) {
        var supplier = semantics.entity("Supplier").entityId();
        var warehouse = semantics.entity("Warehouse").entityId();
        var product = semantics.entity("Product").entityId();
        var riskScore = semantics.field("Supplier", "riskScore");

        var config = new SemanticAuthorizationConfiguration()
                .addEntityRule((ctx, entity, op) -> {
                    if (op != AuthorizationOperation.READ) return false;
                    if (entity.equals(product)) return true;
                    return (entity.equals(supplier) || entity.equals(warehouse))
                            && caller.role() != Authorization.Role.CUSTOMER;
                })
                .addFieldRule((ctx, entity, field, op) -> {
                    if (op != AuthorizationOperation.READ) return false;
                    return !field.equals(riskScore) || Authorization.canReadSupplierRisk(caller);
                })
                .addRelationshipRule((ctx, entity, relationship, op) -> false)
                .addPredicateRule((ctx, entity, op) ->
                        op == AuthorizationOperation.READ && (entity.equals(supplier) || entity.equals(warehouse))
                                ? tenantPredicate()
                                : null);
        return new ConfiguredSemanticAuthorizationPolicy(
                config, new SemanticAuthorizationContext(caller.tenantId(), caller.role().name(), Map.of()));
    }

    /** The values the tenant predicate reads at execution time. */
    public static com.foundgine.core.execution.ExecutionContext executionContext(Authorization.Context caller) {
        return new com.foundgine.core.execution.ExecutionContext(Map.of(TENANT_CONTEXT_KEY, caller.tenantId()));
    }

    private static AuthorizationPredicate tenantPredicate() {
        return AuthorizationPredicate.equal(
                AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), TENANT_ATTRIBUTE),
                AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("context"), "tenantId"));
    }
}
```

- **Entity access.** A `CUSTOMER` may read only `Product`; other roles may read the three mapped
  entities. Writes and relationships are denied.
- **Field access.** `Supplier.riskScore` needs `Authorization.canReadSupplierRisk`. A field the
  caller may not see is dropped from the plan instead of failing the request.
- **Row-level tenant isolation.** Reads of `Supplier` and `Warehouse` carry the predicate
  `resource.tenantId == context.tenantId`. The ORM provider ANDs it onto the query, so a caller
  filter on another tenant returns no rows. The tenant value comes from the execution context
  (`context.tenantId`), and a missing value fails closed.
- **Not enforced:** `allowedWarehouses` (warehouse-level scoping). Add it as another predicate if
  you need it.

### 7b. Semantics: metadata, registry, plan

`jpa/SupplyChainSemantics.java` is persistence-neutral. It turns the generated metadata into an ORM
registry and a frozen contract, then takes a read through authorization, planning and lowering to a
provider plan:

```java
package com.foundgine.samples.supplychain.jpa;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionIRCompiler;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizer;
import com.foundgine.core.semantic.ir.SemanticOperation;
import com.foundgine.core.semantic.ir.SemanticReadNode;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.SemanticModelDiscovery;
import com.foundgine.core.semantic.planning.Planner;
import com.foundgine.core.semantic.query.SemanticFieldFilter;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.samples.supplychain.Authorization;
import com.foundgine.samples.supplychain.generated.GeneratedFoundgineMetadata;
import com.foundgine.providers.storage.orm.OrmPlanCompiler;
import com.foundgine.providers.storage.orm.OrmRegistry;

import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.stream.StreamSupport;

/**
 * The persistence-neutral half of the read path: generated metadata -> ORM registry -> frozen contract, and
 * semantic read -> authorization (with provenance) -> plan -> ExecutionIR -> provider plan. It has no JPA
 * dependency, so it can be exercised against any {@code IOrmQuerySource}. It is the hand-written baseline; the JPA
 * path is {@link SupplyChainOrm}, wrapped by {@link SupplyChainJpa}.
 *
 * <p>The contract is scoped to the mapped entities: the generated metadata for the full domain cannot be turned
 * into a semantic contract because {@code ProductComponent} has a composite key and no {@code id} field, and
 * the ORM provider can only execute mapped entities anyway.
 *
 * <p>Generated semantic field names are PascalCase ({@code RiskScore}); JPA attributes are camelCase
 * ({@code riskScore}). The registry is given that mapping explicitly, and callers may use either casing.
 */
public final class SupplyChainSemantics {
    /** Semantic entity name -> persistent class. Only these entities are queryable through the ORM provider. */
    public static final Map<String, Class<?>> ENTITY_TYPES = Map.of(
            "Supplier", SupplierEntity.class,
            "Warehouse", WarehouseEntity.class,
            "Product", ProductEntity.class);

    private final String provider;
    /** Only the mapped entities (and relationships between them); see the class comment for why. */
    private final MetadataRegistry metadata = scoped(GeneratedFoundgineMetadata.build());
    private final OrmRegistry registry;
    private final SemanticContractSnapshot contract;
    private final Planner planner = new Planner();

    public SupplyChainSemantics(String provider) {
        this.provider = provider;
        var mapped = StreamSupport.stream(metadata.entities().spliterator(), false).toList();
        var relationships = StreamSupport.stream(metadata.relationships().spliterator(), false).toList();
        var attributeNames = new HashMap<String, String>();
        for (var e : mapped) {
            for (var f : e.effectiveFields()) attributeNames.put(e.name() + "." + f.name(), decapitalize(f.name()));
        }
        this.registry = OrmRegistry.create(mapped, relationships, ENTITY_TYPES, attributeNames);
        requireAttributes(registry);
        this.contract = SemanticModelDiscovery.fromMetadata(metadata).build().freeze().createSnapshot();
    }

    public OrmRegistry registry() {
        return registry;
    }

    public EntityMetadata entity(String name) {
        return StreamSupport.stream(metadata.entities().spliterator(), false)
                .filter(x -> x.name().equals(name)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown entity " + name));
    }

    public FieldId field(String entity, String field) {
        return entity(entity).effectiveFields().stream().filter(f -> f.name().equalsIgnoreCase(field)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown field " + entity + "." + field)).id();
    }

    /**
     * Plans a read of {@code fields} from {@code entity} where {@code filterField = value}, authorized for
     * {@code caller} by {@link SupplyChainPolicy}. Execute the result with {@link SupplyChainPolicy#executionContext}.
     */
    public ProviderPlan planRead(
            Authorization.Context caller, String entity, List<String> fields, String filterField, Object value) {
        EntityMetadata e = entity(entity);
        List<FieldId> selected = fields.stream().map(f -> field(entity, f)).toList();
        var options = new SemanticQueryOptions(
                new SemanticFieldFilter(field(entity, filterField), SemanticFilterOperator.EQ, value),
                List.of(), null, null, null);
        var operation = new SemanticOperation(
                new SemanticReadNode(1, e.entityId(), selected, null, null, List.of(), options, null));
        var authorizer = new SemanticAuthorizer(SupplyChainPolicy.create(this, caller));
        var semanticPlan = planner.plan(contract, authorizer.authorizeWithEvidence(contract, operation));
        return new OrmPlanCompiler(provider, registry).compile(ExecutionIRCompiler.compile(semanticPlan));
    }

    /** The provider does not check this: without it a misnamed attribute only fails on the first query. */
    private static void requireAttributes(OrmRegistry registry) {
        for (var mapping : registry.all()) {
            for (var binding : mapping.fields()) {
                if (!hasField(mapping.type(), binding.name())) {
                    throw new IllegalStateException("Semantic entity '" + mapping.name() + "' field " + binding.id()
                            + " is bound to attribute '" + binding.name() + "', which "
                            + mapping.type().getSimpleName() + " does not declare.");
                }
            }
        }
    }

    private static boolean hasField(Class<?> type, String name) {
        for (Class<?> c = type; c != null; c = c.getSuperclass()) {
            try {
                c.getDeclaredField(name);
                return true;
            } catch (NoSuchFieldException ignored) {
                // keep walking up
            }
        }
        return false;
    }

    private static MetadataRegistry scoped(MetadataRegistry all) {
        var out = new MetadataRegistry();
        var ids = new HashSet<EntityId>();
        for (var e : all.entities()) {
            if (ENTITY_TYPES.containsKey(e.name())) {
                out.register(e);
                ids.add(e.entityId());
            }
        }
        for (var r : all.relationships()) {
            if (ids.contains(r.source()) && ids.contains(r.target())) out.register(r);
        }
        return out;
    }

    private static String decapitalize(String s) {
        return s.isEmpty() ? s : Character.toLowerCase(s.charAt(0)) + s.substring(1);
    }
}
```

**What matters here:**

- `GeneratedFoundgineMetadata.build()` is the class the annotation processor emitted from
  `SupplyChainDomain`; the module's `pom.xml` passes
  `-Afoundgine.generated.package=com.foundgine.samples.supplychain.generated`, so that is its
  package (the processor's own default would be `com.foundgine.generated`).
- **Names differ by case.** Generated semantic fields are PascalCase (`RiskScore`); JPA attributes
  are camelCase (`riskScore`). The registry is given an explicit `Entity.Field -> attribute` map,
  and `field(...)` matches names case-insensitively. The tenant predicate's resource member is the
  JPA attribute name (`tenantId`).
- **The contract is scoped to mapped entities.** The full generated metadata cannot become a
  semantic contract: `ProductComponent` has a composite key and no `id` field, so discovery
  rejects it. Only entities that have a JPA class can execute anyway.
- **Attribute names are checked at startup by this class**, not by the provider: `requireAttributes`
  fails fast if a bound attribute does not exist on the JPA class. Without it a misnamed attribute
  only fails on the first query.
- `authorizeWithEvidence` stamps the plan with authorization provenance, and the ORM compiler
  refuses a plan that lacks it.

### 7c. JPA execution on `FoundgineOrm`

The JPA path runs on `FoundgineOrm`, the Java counterpart of the .NET EF Core provider. Two classes
carry it. `jpa/SupplyChainOrm.java` is the fluent configuration: it loads the generated metadata,
entity map and JPA models, and plugs in the authorization policy and execution context.
`jpa/SupplyChainJpa.java` is a thin wrapper that opens the `EntityManagerFactory` and exposes
`read`, `entities`, `openEntityManager` and the `orm()` accessor. Filter values are bound parameters
in the generated query, and the generated models mean no reflective entity access.

```java
package com.foundgine.samples.supplychain.jpa;

import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.samples.supplychain.Authorization;
import com.foundgine.samples.supplychain.generated.GeneratedFoundgineMetadata;
import com.foundgine.samples.supplychain.generated.GeneratedJpaModels;
import com.foundgine.samples.supplychain.generated.GeneratedOrmMaps;

/**
 * The fluent {@link FoundgineOrm} configuration for the starter. It replaces the hand-written entity map,
 * attribute map and contract setup in {@link SupplyChainSemantics} with generated maps and models.
 *
 * <p>UNVERIFIED: not compiled or run where it was written (no Foundgine/Jakarta jars available).
 */
public final class SupplyChainOrm {
    static final FoundgineOrm.Authorization<Authorization.Context> AUTHORIZATION =
            new FoundgineOrm.Authorization<>() {
                @Override
                public ISemanticAuthorizationPolicy policy(Authorization.Context caller, MetadataRegistry metadata) {
                    return SupplyChainPolicy.create(metadata, caller);
                }

                @Override
                public ExecutionContext executionContext(Authorization.Context caller) {
                    return SupplyChainPolicy.executionContext(caller);
                }
            };

    private SupplyChainOrm() {}

    /** Builder pre-loaded with the generated metadata, entity map and models; callers may add to it. */
    public static FoundgineOrm.Builder<Authorization.Context> configure() {
        return FoundgineOrm.<Authorization.Context>configure()
                .metadata(GeneratedFoundgineMetadata.build())
                .entities(GeneratedOrmMaps.entityTypes())
                .generatedModels(GeneratedJpaModels.create())
                .attributeNaming(FoundgineOrm.AttributeNaming.CAMEL_CASE);
    }

    public static FoundgineOrm<Authorization.Context> build() {
        return configure().authorization(AUTHORIZATION).build();
    }
}
```

```java
package com.foundgine.samples.supplychain.jpa;

import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import com.foundgine.providers.storage.orm.jpa.JpaEntities;
import com.foundgine.samples.supplychain.Authorization;

import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Persistence;

import java.util.List;
import java.util.Map;

/**
 * JPA + PostgreSQL execution for the starter, on the fluent {@link FoundgineOrm} configuration
 * ({@link SupplyChainOrm}). Foundgine, not JPA, owns planning and authorization: every read is
 * authorize -> plan -> ExecutionIR -> JPA Criteria, and the generated models mean no reflective entity access.
 *
 * <p>UNVERIFIED: not compiled or run against Hibernate/PostgreSQL (no jars or network where it was written).
 */
public final class SupplyChainJpa implements AutoCloseable {
    private final EntityManagerFactory emf;
    private final FoundgineOrm<Authorization.Context> orm = SupplyChainOrm.build();

    public SupplyChainJpa() {
        this.emf = Persistence.createEntityManagerFactory("foundgine-supply-chain", connectionProperties());
    }

    /** The configured ORM: reader, typed repositories, generic entity gateway, dry-run planning. */
    public FoundgineOrm<Authorization.Context> orm() {
        return orm;
    }

    /**
     * Reads {@code entity} rows (the listed fields) where {@code filterField = value}, as {@code caller}: the
     * policy adds tenant isolation and drops fields the caller may not see. Field names are case-insensitive.
     */
    public List<ExecutionRow> read(
            Authorization.Context caller, String entity, List<String> fields, String filterField, Object value) {
        return orm.reader(emf).read(caller, entity, fields, filterField, value);
    }

    /**
     * Generic, name-addressed access to every mapped entity for {@code caller}. Reads go through the same
     * authorization as {@link #read}; writes are mechanical, so authorize the capability before calling them.
     * The caller owns {@code em} and closes it.
     */
    public JpaEntities entities(Authorization.Context caller, EntityManager em) {
        return orm.entities(caller, em);
    }

    public EntityManager openEntityManager() {
        return emf.createEntityManager();
    }

    private static Map<String, Object> connectionProperties() {
        return Map.of(
                "jakarta.persistence.jdbc.url", env("SUPPLYCHAIN_JDBC_URL", "jdbc:postgresql://localhost:4429/foundgine_supply_chain"),
                "jakarta.persistence.jdbc.user", env("SUPPLYCHAIN_JDBC_USER", "benchmark"),
                "jakarta.persistence.jdbc.password", env("SUPPLYCHAIN_JDBC_PASSWORD", "benchmark"));
    }

    private static String env(String key, String fallback) {
        var v = System.getenv(key);
        return v == null || v.isBlank() ? fallback : v;
    }

    @Override
    public void close() {
        if (emf.isOpen()) emf.close();
    }
}
```

`SupplyChainSemantics` (section 7b) stays as the hand-written, persistence-neutral baseline that
`SupplyChainSemanticsTest` compares against. The JPA path itself does not use it.

### 7d. The test

`src/test/.../SupplyChainSemanticsTest.java` runs `SupplyChainSemantics` and the policy against
`InMemoryOrmQuerySource`. It asserts that:

- a `tenant-a` caller sees only `tenant-a` rows;
- a filter on `tenant-b` returns nothing (the predicate wins over the caller's filter);
- `riskScore` appears for an `ANALYST` and is dropped for a `WAREHOUSE_OPERATOR`;
- a `CUSTOMER` reading suppliers is refused, and so is an unmapped entity;
- a missing tenant value in the execution context fails closed.

```bash
cd src/java
mvn -pl samples/foundgine-supply-chain -am test -Dtest=SupplyChainSemanticsTest -Dsurefire.failIfNoSpecifiedTests=false
```

---

## 8. Wire it together in `Main`

`Main` prints the runtime options, reads the US suppliers visible to an `ANALYST` in
`tenant-a`, then shows the typed repository from the same `FoundgineOrm` configuration. If PostgreSQL is not reachable it prints a "skipped" line instead of failing:

```java
package com.foundgine.samples.supplychain;

import com.foundgine.runtime.*;
import com.foundgine.samples.supplychain.jpa.SupplyChainJpa;

import java.util.List;
import java.util.Set;

public final class Main {
    public static void main(String[] args) {
        FoundgineOptions options = new FoundgineOptions();
        System.out.println("Foundgine Supply Chain Java sample");
        System.out.println("Runtime configured: " + options);

        // JPA + PostgreSQL demo: start the database first (docker compose up -d), then run this class.
        // The caller's tenant and role are enforced by SupplyChainPolicy, not by the filter below.
        var caller = new Authorization.Context("tenant-a", Set.of(1, 2), Authorization.Role.ANALYST, true);
        try (var jpa = new SupplyChainJpa()) {
            var suppliers = jpa.read(caller, "Supplier", List.of("id", "name", "country", "riskScore"), "country", "US");
            System.out.println("tenant-a US suppliers via JPA: " + suppliers.size());
            suppliers.forEach(r -> System.out.println("  " + r.values()));

            // Typed repository from the same FoundgineOrm configuration (mechanical CRUD, generated accessors).
            try (var em = jpa.openEntityManager()) {
                var repo = jpa.orm().repository(em, com.foundgine.samples.supplychain.jpa.SupplierEntity.class);
                System.out.println("suppliers in table (repository.count, unauthorized by design): " + repo.count());
            }
        } catch (RuntimeException e) {
            System.out.println("JPA demo skipped (is PostgreSQL running on SUPPLYCHAIN_JDBC_URL?): " + e);
        }
    }
}
```

Run it:

```bash
cd src/java/samples/foundgine-supply-chain && docker compose up -d && cd ../..
mvn -pl samples/foundgine-supply-chain -am compile exec:java \
  -Dexec.mainClass=com.foundgine.samples.supplychain.Main
```

Expected output ends with `tenant-a US suppliers via JPA: 1` followed by the `Acme Components`
row, including `riskScore`. The `tenant-b` US supplier is excluded by the tenant predicate, not by
the `country` filter, and the `tenant-a` German supplier is excluded by that filter.

---

## 9. Where to go next

You now have the Java domain, authorization shape, adversarial-scenario smoke test and a JPA read
path in place. Still missing compared with the C# starter: the MCP transport and the mutation
repository (`place_order`, `cancel_order`).

For the **complete** MCP → semantic model → authorization → planning/execution → PostgreSQL path,
already implemented in Java, continue with
[`../foundgine-supply-chain-advanced`](../foundgine-supply-chain-advanced/README.md) and its
[step-by-step tutorial](../foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md), which
walks through `AdvancedMcpServer`, `AdvancedMutationPipeline`, and `PostgresSupplyChainStore`.
