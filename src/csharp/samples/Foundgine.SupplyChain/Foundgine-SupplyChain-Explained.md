# Foundgine Supply Chain Starter — Every Concept, Explained

This is a companion to `SupplyChain-Starter-Tutorial.md`. It exists because that
tutorial *shows* you each file, but doesn't always stop to explain *why the
concept exists* or *what you need installed/configured before it will work*.
Read this alongside the tutorial, in the same order (sections match the
tutorial's numbered steps).

---

## 0. The mental model

Foundgine sits between "an AI agent calling a tool" and "SQL running against
your database." Its whole point is that **no layer above the SQL compiler is
allowed to know column names, table names, or write raw SQL** — everything is
expressed as *semantic* operations (read this entity, filtered by this field,
traversing this relationship) that only get turned into SQL at the very last
step, by a compiler that knows your schema.

Why does that matter in practice?
- An LLM-driven agent calling `place_order` can't SQL-inject anything — there
  is no SQL for it to inject into. It can only invoke named, typed capabilities.
- If you rename a column (`email` → `email_address`), you edit **one
  attribute** in `Domain/StorageModels.cs`. Nothing else changes.
- Every query that runs can be fingerprinted (hashed) and logged, so you have
  an audit trail of "what plan actually executed," independent of trusting
  the caller's description of what it asked for.

---

## 1. Prerequisites — what each one is for

| Requirement | Why you need it |
|---|---|
| **.NET 9 SDK** | Foundgine's generator is a Roslyn *source generator*, which only runs inside a .NET/Roslyn compilation. You cannot use an older SDK — Roslyn incremental generators need a modern SDK/compiler. |
| **Docker Desktop** | The sample stores data in real PostgreSQL, not an in-memory fake, so the SQL compiler output is exercised against a real engine (real types, real constraints, real query plans). |
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
- **`Foundgine.Providers`** — the part that *does* know Postgres exists (via
  `Foundgine.Providers.Storage.Sql`), *does* know MCP exists (via
  `Foundgine.Providers.Tools.MCP`), and contains the **AOT generator** as a
  sub-project (`Foundgine.Providers.Aot.Generator`).
- **`Foundgine.Extensions`** — optional, only if you also want a GraphQL
  surface (Hot Chocolate) in addition to/instead of MCP. The starter doesn't
  use it.

**Why a Roslyn analyzer instead of a normal NuGet dependency for the
generator?** Because a source generator has to run *during your build*, as an
`Analyzer` item, not as a regular assembly reference — that's what the
`OutputItemType="Analyzer" / ReferenceOutputAssembly="false"` incantation in
the `.csproj` is doing. It tells MSBuild "run this project's code as a
compiler plugin against my source, but don't link its assembly into my app."

---

## 3–5. Domain models, storage entities, and mappings — why three files, not one

This is the part people most often try to collapse into one file, and it's
worth understanding why the tutorial keeps them separate:

1. **`Domain/Models.cs`** (`[FoundgineModel]`) is the *vocabulary* your
   application and your AI agent talk in — "Customer", "SalesOrder". This
   layer should be stable even if you migrate databases entirely.
2. **`Domain/StorageModels.cs`** (`[FoundgineEntity]` / `[FoundgineField]` /
   `[FoundgineRelationship]`) is the *actual schema* — real table names, real
   column names, real foreign keys. This layer changes whenever your DBA
   changes something.
3. **`Domain/Mappings.cs`** (`[FoundgineModelEntityMap]`) is a **firewall**
   between the two. It's intentionally the *only* file allowed to `using`
   both the `Models` and `Storage` namespaces. If you ever find yourself
   importing `Domain.Storage` from your application layer, that's a sign the
   boundary is leaking.

**IDs matter, and here's the actual rule, precisely:**
- `[FoundgineModel(..., Id = N)]` — unique across all models.
- `[FoundgineEntity(..., Id = N)]` — unique across all entities.
- `[FoundgineField(..., Id = N)]` — unique *within its entity* (two different
  entities can reuse field id `1` for their primary key, that's fine and
  is exactly what the sample does — every `Id` column is field `1`).
- `[FoundgineRelationship(..., Id = N)]` — unique across the **whole model**,
  not just within an entity, because relationships are graph edges that get
  compared against each other during planning.

If you get an id collision, the AOT generator (or `ValidateRelationships`,
which runs before code emission) will fail the build with a diagnostic
rather than silently producing wrong metadata — this is deliberate: wrong
metadata here means wrong SQL later, so the generator fails loudly and early.

**Checkpoint discipline:** the tutorial tells you to `dotnet build` right
after step 5, before writing any application code. Do this. If your
attributes are malformed, you want the compiler to tell you *now*, not three
files later when a query mysteriously returns nothing.

---

## 6. Why there's no semantic-model file to write — and why the mapping still is

### The short version
Earlier revisions of this sample had you hand-write
`Semantics/SupplyChainSemanticModel.cs` — a "front door" file so nothing in
`Infrastructure`/`Application` imported `Foundgine.Generated` directly or
juggled raw numeric ids. That file is now **gone entirely**. Application
code (`SupplyChainQueryRepository`, `SupplyChainMutationRepository`,
`Program.cs`) references `Foundgine.Generated.GeneratedMetadata` and
`Foundgine.Generated.GeneratedSemanticModel` directly. There's nothing left
for the wrapper to do.

### Why it became unnecessary
The wrapper originally earned its place two ways:

1. **`EntityId` passthrough properties** (`Customer`, `SalesOrder`, …) — low
   risk, but boilerplate.
2. **Relationship lookups** — the file used to run a LINQ query against the
   whole `MetadataRegistry` at static-constructor time, matching on
   **string names**:

   ```csharp
   private static RelationshipId Relationship(string entityName, string relationshipName) =>
       Registry.Relationships
           .Single(x => x.Name == relationshipName &&
                        Registry.GetEntity(x.Source).Name == entityName)
           .Id;

   public static readonly RelationshipId CustomerOrders = Relationship("CustomerERP", "Orders");
   ```

   This didn't scale: every new relationship needed a new hand-typed call,
   the entity/relationship names were plain strings with no compiler check,
   and a typo failed at **runtime** via `Single()` throwing.

We extended the AOT generator itself
(`src/csharp/Foundgine.Providers/Foundgine.Providers.Aot.Generator/FoundgineMetadataGenerator.cs`,
in `EmitSemanticModel`) so it emits a `Relationships` nested class directly
under each model's generated class — one strongly-typed constant per
`[FoundgineRelationship]` property found on that model's mapped storage
entity:

```csharp
// auto-generated, in Foundgine.Generated.GeneratedSemanticModel
public static class Customer
{
    public static readonly EntityId Entity = new(101);
    // ...fields...

    public static class Relationships
    {
        public static readonly RelationshipId Orders = new(1);
    }
}
```

Once that existed, the wrapper's *entire remaining content* was one-line
aliases with zero logic in them — `SupplyChainSemanticModel.Customer =>
GeneratedSemanticModel.Customer.Entity`, `SupplyChainSemanticModel.Metadata
=> GeneratedMetadata.Registry`, and so on. A file that only renames things
one-to-one isn't a boundary, it's indirection — so we deleted it and updated
the three call sites that used it (`Program.cs`'s two DI registrations,
and the two relationship references inside `SupplyChainQueryRepository`) to
name the generated members directly:

```csharp
// Program.cs — GeneratedMetadata.Registry already implements IMetadataProvider
builder.Services.AddSingleton<IMetadataProvider>(GeneratedMetadata.Registry);
builder.Services.AddSingleton(GeneratedMetadata.Registry);
```

```csharp
// SupplyChainQueryRepository.cs
GeneratedSemanticModel.SalesOrder.Relationships.Lines
GeneratedSemanticModel.Shipment.Relationships.Order
```

**What this buys you as you onboard new entities:** add a
`[FoundgineRelationship]` property, rebuild, and the accessor
(`GeneratedSemanticModel.<Model>.Relationships.<Name>`) just *exists* —
no wrapper file to touch, no lookup call to write, and a typo is a compile
error (unknown member) instead of a `Single()` throw at app startup.

### The mapping is still required — this is the important part

None of the above means `Domain/Mappings.cs` became optional. It's the
opposite: `Domain/Mappings.cs` is now the **only** place that decides
whether a `GeneratedSemanticModel.<Model>` class gets emitted at all. The
generator registers every `[FoundgineEntity]` into
`GeneratedMetadata.Registry` unconditionally, but it only emits the
`GeneratedSemanticModel.<Model>` class — the one with `.Entity`, field
constants, and `.Relationships` — for models that appear in a
`[FoundgineModelEntityMap]`:

```csharp
// FoundgineMetadataGenerator.cs, EmitSemanticModel
if (!modelEntityMap.TryGetValue(model.ToDisplayString(), out var entity) || ...)
    continue; // this model gets no GeneratedSemanticModel class at all
```

Skip the mapping for a model and you still get raw metadata for its entity
(plannable by name), but you lose every compile-time-checked accessor for
it. So removing the hand-written wrapper made the mapping *more* load-bearing,
not less: with no wrapper standing between application code and the
generator's output, `Domain/Mappings.cs` is the one file that determines
what application code is even allowed to reference by name.

### Where this lives

- Generator change: `src/csharp/Foundgine.Providers/Foundgine.Providers.Aot.Generator/FoundgineMetadataGenerator.cs`
- Removed: `src/csharp/samples/Foundgine.SupplyChain/Semantics/SupplyChainSemanticModel.cs`
  (the `Semantics/` folder no longer exists in this sample)
- Updated call sites: `src/csharp/samples/Foundgine.SupplyChain/Program.cs`,
  `src/csharp/samples/Foundgine.SupplyChain/Infrastructure/Queries/SupplyChainQueryRepository.cs`

### Build the solution

```bash
cd src/csharp/samples/Foundgine.SupplyChain
dotnet build
```

Then inspect the generated file (path will be under
`obj/Debug/net9.0/generated/.../FoundgineMetadataGenerator/...`, or just
right-click → "Go to Definition" on `GeneratedSemanticModel` in your IDE) and
confirm you see a `Relationships` nested class with the expected members.
Also run:

```bash
dotnet test src/csharp/tests/Foundgine.Aot.Tests
```

---

## 7. Application layer — the concepts worth understanding

- **Capability, not endpoint.** MCP tools are named verbs (`place_order`),
  not REST routes. The authorization model (`ICapabilityAuthorizer.Demand`)
  is written around "is this actor allowed to invoke this capability," which
  maps naturally onto that.
- **`actor` + `token` on every single call.** This is deliberately
  *stateless* — there's no session, no cookie. Every MCP tool call must prove
  identity fresh. That's what makes `o.Stateless = true` in `Program.cs`
  consistent with the auth model.
- **Ownership checks are separate from capability checks.** `Demand()` does
  two different things: "is `alice` allowed to call `get_order` at all" and,
  separately, "is `alice` allowed to call it *for customerId=2*." Collapsing
  these into one check is a common real-world bug (you can end up granting
  capability-holders access to *any* customer's data by accident) — the
  sample keeps them as two distinct steps precisely to avoid that.
- **Constant-time token comparison (`FixedTimeEquals`).** A naive `token ==
  expectedToken` string comparison in C# short-circuits on the first
  mismatched character, which leaks *how many characters were correct* via
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
  it's checking a *dependency*, not just the process. Point your
  orchestrator's readiness probe at `/health/ready`, and its liveness probe
  at `/health`.
- **The seed schema is the *only* place table/column names need to match
  your `StorageName` attributes.** Nothing else in the app cares what the
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

1. Install .NET 9 SDK + Docker; verify both.
2. Clone, `dotnet build` the whole solution once to prime NuGet/Roslyn caches.
3. Read `Domain/Models.cs` → `Domain/StorageModels.cs` → `Domain/Mappings.cs`,
   in that order — that's the dependency direction of understanding, not just
   of code.
4. `dotnet build` again — confirm `GeneratedSemanticModel` and (with the
   change above) its new `Relationships` nested classes appear.
5. `docker compose up -d postgres`, run `seed.sql`.
6. `dotnet run`, hit `/health/ready`.
7. Call `describe_capabilities` for each of `alice`/`bob`/`carol`/`dave`/`admin`
   first, before anything else — it's the fastest way to see the
   authorization model in action without touching the database at all.
