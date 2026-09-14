# Foundgine Supply Chain Starter (Java) — Every Concept, Explained

This is the Java companion to
[`Foundgine-SupplyChain-Explained.md`](../../csharp/samples/Foundgine.SupplyChain/Foundgine-SupplyChain-Explained.md)
and to [`SupplyChain-Starter-Tutorial.md`](./SupplyChain-Starter-Tutorial.md) in this folder. It
exists because the tutorial *shows* you each file, but doesn't always stop to explain *why the
concept exists* or *what's different from the C# version*. Read this alongside the tutorial, in the
same order (sections match the tutorial's numbered steps).

---

## 0. The mental model

The mental model is identical to the C# sample: Foundgine sits between "an AI agent calling a tool"
and "SQL running against your database," and **no layer above the SQL compiler is allowed to know
column names, table names, or write raw SQL** — everything is expressed as *semantic* operations
that only get turned into SQL at the last step. Java doesn't change that boundary; it changes the
mechanism used to declare it at compile time (annotation processing instead of Roslyn source
generation) and the idioms used to express it (`record`s and static factory methods instead of
`init`-only properties and attributes with named arguments).

---

## 1. Prerequisites — what each one is for

| Requirement | Why you need it |
|---|---|
| **JDK 21** | The `foundgine-aot-generator` module is a Java **annotation processor** (`javax.annotation.processing.Processor`), which is the JDK's compile-time code-generation mechanism — the Java analogue of a Roslyn source generator. Annotation processors run as part of `javac`, so any reasonably current JDK works; this reactor pins `maven.compiler.release` to 21. |
| **Maven 3.9+** | The Java port is a standard multi-module Maven reactor (`src/java/pom.xml` as parent, one child module per package, `samples/*` for sample applications) — the Java analogue of the `.sln` + `<ProjectReference>` graph on the C# side. |
| **Docker with Compose** | Once you move past the domain/authorization port covered by this starter and into the fully wired [advanced sample](../foundgine-supply-chain-advanced/README.md), data is stored in real PostgreSQL, not an in-memory fake, so the generated SQL is exercised against a real engine. |
| **Git clone with reactor references** | Every module and sample in `src/java` depends on `${revision}` — a single Maven property in `src/java/pom.xml` — instead of a published Maven Central version, so you always build against the exact source in the repo, the same reasoning the C# tutorial gives for using `<ProjectReference>` over a NuGet version. |

---

## 2. The 4 artifacts — what problem each one solves

The Java port publishes the same four architectural boundaries as the .NET packages, as Maven
artifacts under `io.github.cristianbarragan`:

- **`foundgine-core`** — pure contracts and data structures: `EntityId`, `FieldId`,
  `RelationshipId`, the semantic model/IR, and the planner. No I/O, no JDBC driver, no HTTP. This is
  what makes Foundgine provider-neutral in Java exactly as it is in .NET: `foundgine-core` doesn't
  know PostgreSQL exists.
- **`foundgine-runtime`** — the orchestration layer that executes a plan and exposes
  application-facing APIs (`FoundgineOptions`, the mutation engine used by the advanced sample).
  Sits between Core and Providers.
- **`foundgine-providers`** — the part that *does* know PostgreSQL exists, *does* know MCP exists,
  and hosts the AOT annotation-processor sub-module (`foundgine-aot-generator`) as a Maven module of
  its own, mirroring `Foundgine.Providers.Aot.Generator`.
- **`foundgine-extensions`** — optional caller-facing adapters. Not used by this starter.

For a normal application, the minimum footprint is `foundgine-runtime` + `foundgine-providers`;
`foundgine-core` is a transitive dependency of both. See
[`docs-site/packages/index.html`](../../../../docs-site/packages/index.html) for the current Maven
Central coordinates.

---

## 3. Why `record`s instead of mutable classes

The C# starter declares its domain model as `sealed class`es with `init`-only properties
(`public int Id { get; init; }`). The Java port uses `record`s instead
(`public record Product(int id, String sku, ...)`). Both choices express the same intent — the
domain model is immutable data, constructed once and never mutated in place — but `record`s are the
idiomatic Java mechanism for that, and they let `@FoundgineEntity` read the entity's fields directly
from the record's canonical components at annotation-processing time instead of needing separate
getter/setter reflection.

## 4. `@FoundgineSemanticDimension` — a Java-side addition

`SupplyChainDomain` tags several components with `@FoundgineSemanticDimension("tenant")`,
`@FoundgineSemanticDimension("country")`, and similar. This has no direct analogue in the C#
starter's basic domain, but it is not a divergence in intent: it is the same idea the C# **advanced**
sample expresses through its semantic model's field aliases and authorization predicates
(`Supplier.Country`, tenant-scoped reads). Declaring the dimension on the domain type up front means
the authorization layer and the grounding/retrieval layer described in the advanced sample's docs
can reason about "this field means tenant" without re-deriving it from naming conventions.

## 5. Why the starter's authorization is a `record`, not a policy object

`Authorization.Context` here is a small immutable value (tenant, allowed warehouses, role,
read-only flag) checked by static predicate methods (`canWrite`, `canReadWarehouse`). The C# starter
instead uses an injectable `ICapabilityAuthorizer` service with an actor/token credential store.
Neither shape is "the" Foundgine way — Foundgine's authorization boundary
(`ConfiguredSemanticAuthorizationPolicy` in `foundgine-core`) accepts rules expressed however your
application finds natural; the starter's `Context` record is simply the smallest thing that lets
`Scenarios.assertAdversarialInvariants()` demonstrate tenant isolation without pulling in the full
policy machinery. The [advanced sample's `SupplyChainAuthorization`](../foundgine-supply-chain-advanced/README.md)
shows the same tenant/role concepts wired into Foundgine's actual authorization policy, enforced by
the planner rather than by application code remembering to call a predicate.

## 6. Why cycle detection matters for `recursiveSupplierRisk`

A bill-of-materials graph is exactly the kind of structure an adversarial or malformed caller can
turn into a denial-of-service vector: if `walk` only tracked "have I visited this node before, ever,"
a diamond-shaped (but acyclic) BOM would be reported as a false cycle; if it tracked nothing at all,
a genuine cycle (`1 → 2 → 3 → 1`) would recurse forever. Tracking the *current path* separately from
*everything ever visited* gets both right, and is the same invariant the advanced sample's live,
PostgreSQL-backed `RecursiveSupplierRiskParityTest` proves holds under the real semantic model and
authorization policy, not just the in-memory fixture here.

---

## 7. Where this leaves you

By the end of the tutorial you have exactly what the C# starter has by its own step 6: a compiled,
annotation-processor-generated semantic model, plus an authorization shape and a smoke test proving
the adversarial invariants hold. The next concepts — MCP as the transport, a query/mutation
repository translating semantic operations into SQL, and `Program.cs`'s dependency wiring — are
already implemented in Java in the [advanced sample](../foundgine-supply-chain-advanced/README.md),
because that is where this port's MCP server (`AdvancedMcpServer`) and PostgreSQL store
(`PostgresSupplyChainStore`) live today. Its own
[step-by-step tutorial](../foundgine-supply-chain-advanced/SupplyChain-Advanced-Tutorial.md) picks
up exactly where this one leaves off.
