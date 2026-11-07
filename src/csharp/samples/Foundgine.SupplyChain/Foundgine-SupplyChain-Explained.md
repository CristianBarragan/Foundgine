# Foundgine Supply Chain Starter — Every Concept, Explained

This is a companion to `SupplyChain-Starter-Tutorial.md`. It exists because that
tutorial _shows_ you each file, but doesn't always stop to explain _why the
concept exists_ or _what you need installed/configured before it will work_.
Read this alongside the tutorial, in the same order (sections match the
tutorial's numbered steps).

---

## 0. The mental model

Foundgine sits between "an AI agent calling a tool" and "SQL running against
your database." Its whole point is that **no layer above the SQL compiler is
allowed to know column names, table names, or write raw SQL** — everything is
expressed as _semantic_ operations (read this entity, filtered by this field,
traversing this relationship) that only get turned into SQL at the very last
step, by a compiler that knows your schema.

Why does that matter in practice?

- An LLM-driven agent calling `place_order` can't SQL-inject anything — there
  is no SQL for it to inject into. It can only invoke named, typed capabilities.
- If you rename a column (`email` → `email_address`), you edit **one
  `HasColumnName(...)` call** in `Domain/PersistenceConfigurations.cs`. Nothing else changes.
- Every query that runs can be fingerprinted (hashed) and logged, so you have
  an audit trail of "what plan actually executed," independent of trusting
  the caller's description of what it asked for.

---

## 1. Prerequisites — what each one is for

| Requirement                     | Why you need it                                                                                                                                                                                                                        |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **.NET 10 SDK**                 | The sample project targets `net10.0`, so it needs the .NET 10 SDK to build. Foundgine's fluent configuration reads member names out of C# expression trees, which every current SDK supports; the SDK version is about the sample's target framework, not the API. |
| **Docker Desktop**              | The sample stores data in real PostgreSQL, not an in-memory fake, so the SQL compiler output is exercised against a real engine (real types, real constraints, real query plans).                                                      |
| **Git clone with project refs** | Because `Foundgine.Core/Runtime/Providers` are still evolving alongside the sample, the tutorial deliberately uses `<ProjectReference>` instead of published NuGet versions, so you always build against the exact source in the repo. |

**Verify** with `dotnet --version` / `docker --version` before doing anything
else — nearly every "weird build error" in this kind of project traces back
to an SDK version mismatch or Docker not running.

---

## 2. The 4 packages — what problem each one solves

- **`Foundgine.Core`** — pure contracts and data structures: `EntityId`,
  `FieldId`, `RelationshipId`, the semantic IR (intermediate representation),
  and the `Planner`. No I/O, no database driver, no HTTP. This is what makes
  Foundgine provider-neutral: `Foundgine.Core` doesn't know Postgres exists.
- **`Foundgine.Runtime`** — the orchestration layer that actually executes a
  plan and exposes application-facing APIs (execution context, control
  plane). Sits between Core and Providers.
- **`Foundgine.Providers`** — the part that _does_ know Postgres exists (via
  `Foundgine.Providers.Storage.Sql`), _does_ know MCP exists (via
  `Foundgine.Providers.Tools.MCP`), and contains the **AOT generator** as a
  sub-project (`Foundgine.Providers.Aot.Generator`).
- **`Foundgine.Extensions`** — optional, only if you also want a GraphQL
  surface (Hot Chocolate) in addition to/instead of MCP. The starter doesn't
  use it.

**Where's the model compiler?** There isn't a build-time one in this sample.
The model is described with the **fluent configuration API** in
`Foundgine.Core.Semantic.Configuration` and compiled once at startup by
`new SupplyChainModel().Build()`. The `Foundgine.Providers.Aot.Generator`
Roslyn analyzer (which turns `[FoundgineModel]` / `[FoundgineEntity]`
attributes into metadata at build time) is still referenced from the csproj
as an `Analyzer` item, but a fluent model has no attributes for it to read, so
it emits nothing here. Use it if you prefer attributes, or when you need
Native AOT with no startup work; the two styles produce the same
`MetadataRegistry` and can be mixed with `BuildInto(...)`.

---

## 3–5. Domain models, storage entities, and configuration — why separate files

This is the part people most often try to collapse into one file, and it's
worth understanding why the tutorial keeps them separate. With the fluent API
the types themselves are **plain classes**; everything Foundgine needs to know
lives in small configuration classes, each owning exactly one concern:

| Concern | File | Base class |
| --- | --- | --- |
| Semantic entity name and relationships | `Domain/ModelConfigurations.cs` | `FoundgineModelConfiguration<TModel>` |
| Table, key and column names | `Domain/PersistenceConfigurations.cs` | `FoundginePersistenceConfiguration<TEntity>` |
| Model member → storage member | `Domain/Mappings.cs` | `FoundgineEntityMap<TModel, TEntity>` |
| Relationship → physical foreign key | `Domain/Mappings.cs` | `FoundgineConnectionConfiguration` |
| Model member → storage entity, conversions, authorization | _(not needed by the starter)_ | `FoundgineModelConnectionConfiguration<TModel>` |

and one class registers them all:

1. **`Domain/Models.cs`** is the _vocabulary_ your application and your AI
   agent talk in — "Customer", "SalesOrder". It carries no Foundgine
   attributes and no dependency on `Foundgine.Providers`, so it stays stable
   even if you migrate databases entirely. The navigation properties exist
   only so configuration can name a relationship with a lambda; Foundgine
   never populates them.
2. **`Domain/StorageModels.cs`** is the _actual schema_ — plain scalar
   members for each table. Real table and column names come from
   `PersistenceConfigurations.cs`, which changes whenever your DBA changes
   something.
3. **`Domain/Mappings.cs`** is a **firewall** between the two. It's
   intentionally the _only_ file allowed to `using` both the `Models` and
   `Storage` namespaces. If you ever find yourself importing
   `Domain.Storage` from your application layer, that's a sign the boundary
   is leaking.
4. **`Domain/SupplyChainModel.cs`** is the single registration point
   (`FoundgineModel`). Order does not matter.

**IDs are derived, not assigned.** The old attribute version made you pick a
unique number for every model, entity, field and relationship. The fluent
compiler derives them from names instead: entity ids from the semantic entity
name (`ToEntity("SalesOrderERP")`), field ids from the entity name plus the
member name, relationship ids from the entity name plus the navigation name,
and column ids from `schema.table` plus the column name. The same names always
give the same ids, so fluent and attribute metadata for the same names agree.
A rename therefore changes an id — which is exactly what the checked handles
in step 6 are for.

**Defaults, so you write less:** the semantic entity name defaults to the model
type name, the table to the storage type name, and a column to the member name.
`HasForeignKey` selects the key on the target storage type
(`Customer 1 - * SalesOrder`); `HasForeignKeyOnSource` selects it on the
source (`SalesOrderLine * - 1 CatalogProduct`). One connection configures both
directions of a pair.

**Validation is all-at-once.** `Build()` checks the whole set and throws a
single `InvalidOperationException` listing every problem — a missing key, an
unmapped relationship, an ambiguous connection, mismatched types, or an
unsupported shape such as a composite key. Wrong metadata here means wrong SQL
later, so it fails loudly and early. Unlike the attribute generator, this
happens when the application starts (or when a test calls `Build()`), not at
compile time, so the tutorial's checkpoint after step 5 is to build **and**
run a test or start the app.

---

## 6. Why `SupplyChainSemantics` exists — and what it checks

Application code should not juggle raw ids like
`EntityId.Create("SalesOrderERP")`. `Domain/SupplyChainSemantics.cs` is a small
hand-written class of typed handles (`.Entity`, field accessors with
`.Eq(...)` / `.Set(...)` / `.Asc()`, and `Relationships.<Name>` ids).

Its important property is that **every handle is verified against the compiled
registry the first time its class is used**:

- `EntityOf(name)` throws if no semantic entity with that name was registered
  (so a rename in `ToEntity(...)` can't silently strand the handle).
- `FieldOf(...)` throws if the member isn't mapped on that entity.
- `RelationshipOf(...)` throws if the relationship isn't configured.

Each message says which configuration to update. That replaces the
compile-time safety the AOT generator used to give for attribute-declared
models (it emitted a `GeneratedSemanticModel` class for you); a fluent model
declares nothing for the generator to read, so this thin class takes over.
It's ordinary C# over the registry — no tooling step — and the ids it uses are
the same ones the fluent compiler assigns.

### Going further: connections, conversions and authorization

The starter only needs relationships. When a model should _visit_ a storage
entity by a key that needs converting, or when following that connection needs
an authorization check, use `FoundgineModelConnectionConfiguration<TModel>`:

```csharp
connections.HasConnection<CustomerERP>(x => x.Customer)
    .Maps(x => x.CustomerCode, e => e.CodeNumber, int.Parse)   // conversion
    .Authorize<UserContext>("CanVisit",
        (user, customer) => user.TenantId == customer.TenantId);
```

The conversion is recorded as `ConversionMetadata` for the type pair; Foundgine
never calls the delegate. `Authorize` reads the lambda as an expression tree
(member access, constants, `==`, `!=`, `&&`, `||`, `!`) and `Build()` rejects
anything else.

### Known limits (as of 2.3.0)

- Composite primary keys and composite foreign keys aren't supported.
- Conversions exist on model connections only; `Property(...).To(...)` in an
  entity map needs identical types.
- Connection fields are not matched by name automatically; list each `Maps(...)`.
- `FoundgineModel` subclasses are not discovered by the source generator, so
  `Build()` runs at startup.

---

## 7. Application layer — the concepts worth understanding

- **Capability, not endpoint.** MCP tools are named verbs (`place_order`),
  not REST routes. The authorization model (`ICapabilityAuthorizer.Demand`)
  is written around "is this actor allowed to invoke this capability," which
  maps naturally onto that.
- **`actor` + `token` on every single call.** This is deliberately
  _stateless_ — there's no session, no cookie. Every MCP tool call must prove
  identity fresh. That's what makes `o.Stateless = true` in `Program.cs`
  consistent with the auth model.
- **Ownership checks are separate from capability checks.** `Demand()` does
  two different things: "is `alice` allowed to call `get_order` at all" and,
  separately, "is `alice` allowed to call it _for customerId=2_." Collapsing
  these into one check is a common real-world bug (you can end up granting
  capability-holders access to _any_ customer's data by accident) — the
  sample keeps them as two distinct steps precisely to avoid that.
- **Constant-time token comparison (`FixedTimeEquals`).** A naive `token ==
expectedToken` string comparison in C# short-circuits on the first
  mismatched character, which leaks _how many characters were correct_ via
  response timing. This is a real (if narrow) attack — the fixed-time
  compare closes it.
- **Same error message whether the actor exists or not.** Otherwise you've
  built an oracle that lets someone enumerate valid usernames.

---

## 8. Infrastructure — planner, compiler, fingerprint

- **`Planner`** turns your declarative `SemanticOperation` (read this entity,
  with this filter, including this nested relationship) into a
  provider-neutral execution plan — it doesn't know about SQL at all yet.
- **`SqlCompiler`** is the provider-specific step that turns that plan into
  actual parameterized SQL text + parameter bindings for Postgres.
- **Fingerprint.** `SemanticSqlQueryExecutor` hashes the compiled SQL +
  parameter values (SHA-256, truncated to 24 hex chars) and returns it
  alongside the data. This is your audit trail: given a fingerprint, you can
  prove exactly which SQL text and which parameter values produced a given
  response, without having to trust a log line that could have been written
  incorrectly.
- **Why mutations get a separate compiler.** Reads only need to know "what to
  fetch." Writes need to know "in what order" (insert the order before its
  line items), "what to check first" (is there enough inventory?), and "how
  to make retries safe" (the `idempotencyKey` on `place_order` — if the same
  key is replayed, e.g. because a network call timed out and the agent
  retried, the mutation compiler can recognize that and avoid double-charging
  the customer / double-decrementing stock).

---

## 9–11. Wiring, Postgres, running it — setup concepts

- **`Stateless = true` on the MCP HTTP transport** means every request is
  handled independently — no server-side session state tied to a connection.
  This matches "every call carries its own actor+token" from step 7.
- **`/health` vs `/health/ready`.** `/health` just says the process is up.
  `/health/ready` actually opens a Postgres connection and runs `SELECT 1` —
  it's checking a _dependency_, not just the process. Point your
  orchestrator's readiness probe at `/health/ready`, and its liveness probe
  at `/health`.
- **The seed schema is the _only_ place table/column names need to match
  your `ToTable(...)` / `HasColumnName(...)` configuration.** Nothing else in the app cares what the
  columns are called — that's the whole point of the `Domain/StorageModels.cs`
  / `Domain/Mappings.cs` split from step 4–5.
- **Docker port `4429`.** Arbitrary — chosen to avoid colliding with a
  default local Postgres on `5432`. Change it freely, just keep the
  connection string in `appsettings.json` / the environment variable in sync.

---

## 12. Where the starter stops, on purpose

The tutorial is explicit that it's not implementing:

- Claim-based (as opposed to capability-name-based) authorization,
- The full `PlaceOrder` inventory/idempotency guarantee logic,
- Ambiguity resolution for vague natural-language questions,
- Fuzzy/full-text/graph retrieval,
- Adversarial security testing.

All of those exist in the repo already, in
`src/csharp/samples/Foundgine.SupplyChain.Advanced`, if/when you're ready to go past the
starter.

---

## Quick checklist for onboarding someone new to this project

1. Install .NET 10 SDK + Docker; verify both.
2. Clone, `dotnet build` the whole solution once to prime NuGet/Roslyn caches.
3. Read `Domain/Models.cs` → `Domain/StorageModels.cs` →
   `Domain/PersistenceConfigurations.cs` → `Domain/ModelConfigurations.cs` →
   `Domain/Mappings.cs` → `Domain/SupplyChainModel.cs`, in that order — that's the dependency direction of understanding, not just
   of code.
4. `dotnet build` again, then call `new SupplyChainModel().Build()` (or start
   the app) — confirm it returns without a validation error.
5. `docker compose up -d postgres`, run `seed.sql`.
6. `dotnet run`, hit `/health/ready`.
7. Call `describe_capabilities` for each of `alice`/`bob`/`carol`/`dave`/`admin`
   first, before anything else — it's the fastest way to see the
   authorization model in action without touching the database at all.
