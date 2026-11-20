# Foundgine vs Spring GraphQL + JPA

The Java counterpart to `src/csharp/samples/Foundgine.Comparison`: one shop application, one legacy PostgreSQL schema, two implementations.

The comparison is about architecture, not a claim that Foundgine replaces Spring, Hibernate or JPA. Foundgine adds a configurable semantic and execution boundary above the persistence provider.

## Shared behavior

- Same PostgreSQL schema and seed data (`db/init.sql`).
- Same GraphQL-only shop API.
- Same JWT bearer issuer, audience and claims.
- Same manager/customer authorization rules.
- Same parity scenarios in `tools/parity.py`.

Rules: managers read all rows; customers read only their own Customer row and Orders derived from the `customer_id` claim, can reach Products and OrderLines through those Orders, and cannot start at `orderLine`. Invalid/missing JWT returns 401; missing `tenant_id` is denied.

## Status — source checkpoint

| Part | State in this archive |
|---|---|
| `spring-shop/` baseline | Written; build/runtime parity not verified in this checkpoint |
| `foundgine-shop/` | Written; build/runtime parity not verified in this checkpoint |
| Java GraphQL HTTP handler | Reusable extension implementation; sample integration has not been verified against resolved dependency jars |
| `tools/parity.py` | Two-app comparison and one-URL expectation mode are present; not executed here |

The prior source notes report that Maven and a full dependency classpath were unavailable when the sample was first authored. A syntax-only pass is not a substitute for Maven compilation or integration testing. Do not treat the behavior or security assertions below as runtime-verified until `./run.sh` and the parity checks pass.

## Fluent configuration is the Foundgine model

`foundgine-shop/.../ShopModel.java` is the central Foundgine declaration. It uses fluent configuration classes for four distinct concerns:

| Concern | Configuration in `ShopModel` | Responsibility |
|---|---|---|
| Semantic model | `FoundgineModelConfiguration<T>` | Entity names, fields and navigable relationships |
| Persistence | `FoundginePersistenceConfiguration<T>` | Tables, keys and legacy column names |
| Model mapping | `FoundgineEntityMap<TModel,TEntity>` | Mapping semantic model members to storage members |
| Connections | `FoundgineConnectionConfiguration` | Foreign keys and relationship direction |

Example:

```java
static final class OrderTable extends FoundginePersistenceConfiguration<Order> {
    @Override public void configure(FoundginePersistenceBuilder<Order> m) {
        m.toTable("tbl_order");
        m.hasKey(Order::id);
        m.property(Order::id).hasColumnName("ord_id");
        m.property(Order::customerId).hasColumnName("cust_id");
        m.property(Order::total).hasColumnName("ord_total");
    }
}
```

The semantic model is not defined by GraphQL schema declarations or by JPA relationships. `ShopModel` describes the application-facing model and its relationships; JPA/Hibernate remains the persistence implementation.

### Why the Java persistence classes still have annotations

This checkpoint uses two different annotation systems for two different jobs:

- `jakarta.persistence` annotations (`@Entity`, `@Table`, `@Id`, `@Column`) are Hibernate/JPA mapping metadata.
- `@FoundgineOrmEntity` is currently a Foundgine AOT-discovery marker. The Java annotation processor uses it to generate `GeneratedOrmMaps` and `GeneratedJpaModels`, which `Main` supplies to the ORM provider.

The Foundgine semantic model, table/column declarations, and foreign-key relationships are still configured fluently in `ShopModel`. The marker is not the semantic configuration API; it is part of the current compile-time AOT registration path.

**Important:** this archive does not yet demonstrate a fully annotation-free Foundgine Java persistence registration path. Removing `@FoundgineOrmEntity` without replacing the processor's discovery mechanism would break generated ORM registration. The correct follow-up is an explicit compile-time registration mechanism that preserves generated direct-access models and AOT behavior. Do not claim that this sample has already achieved that.

### Model-to-storage mapping

The sample currently declares identity maps (`Customer -> Customer`, etc.) while the JPA entity classes are separately registered by generated metadata and name. These identity maps are explicit because this sample's semantic model and mapped model members have the same names and types. They are repetitive and should not be represented as a unique advantage. A future refinement should either support a validated identity-map convention or show a genuinely different legacy storage shape where explicit member mapping adds value. Do not remove these declarations until the provider's registration and mapping requirements are covered by tests.

## What the comparison is intended to demonstrate

| Capability | Foundgine Java sample | Spring baseline |
|---|---|---|
| Model relationships | Declared in fluent `ShopModel` configurations | Application resolver/data-access code |
| Legacy table/column mapping | Fluent persistence configuration | JPA annotations and mapping code |
| Filtering and sorting | Derived from the configured model | Explicit filter/controller and GraphQL schema definitions |
| Authorization | Foundgine policy participates in plan execution | Spring Security plus controller/data-access scoping |
| Persistence | JPA/Hibernate provider | JPA/Hibernate |
| API | Foundgine GraphQL adapter | Spring for GraphQL |

This is an architectural comparison, not a performance result. The filter dialects differ and the parity script is intended to compare equivalent behavior rather than identical syntax.

## Measured source size (from the checkpoint's original count)

| Measure | Foundgine shop | Spring shop |
|---|---:|---:|
| Java sample lines, non-blank/non-comment | 441 | 329 |
| Other files noted in the original count | `persistence.xml`: 19 | GraphQL SDL: 24; `application.yml`: 14 |

Foundgine has more application code in this small sample. The useful argument is not fewer lines. It is that semantic relationships, storage mapping and execution policy are declared in a reusable model rather than reimplemented for each API surface.

## Run

```bash
./run.sh
./run.sh check
./run.sh foundgine
```

Requires Docker, JDK 21 and Maven. The first command starts PostgreSQL, both applications and the parity comparison. `check` runs baseline expectations; `foundgine` runs the Foundgine application alone.

## Caveats

- No build, runtime or parity result is asserted by this checkpoint README; execute the commands above before publishing functional claims.
- The JPA relationship traversal path is marked `correlated-exists-bounded-depth-source-unverified` in `../../conformance/orm-jpa-parity-plan.json`.
- Warrant signing and replay storage are ephemeral/in-memory in this local sample; multi-instance production use needs durable shared infrastructure and managed keys.
- The Java comparison does not currently prove an annotation-free AOT registration experience.
- This is not a performance benchmark.

## The point of the comparison

Foundgine is intended to let a team configure application meaning, persistence mapping, relationships and authorization once, then expose those capabilities through different protocols and callers. The Java sample demonstrates the fluent model configuration today, while retaining a Foundgine-specific AOT marker for persistence-class discovery. That distinction should remain explicit until a fluent/compile-time registration alternative is implemented and tested.
