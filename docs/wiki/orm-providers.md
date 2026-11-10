# ORM providers (EF Core and JPA)

> **In plain words.** Many teams already have an ORM. Instead of making them hand-write SQL, Foundgine can hand its plans to **EF Core** (.NET) or **JPA** (Java). The ORM turns the plan into database queries. The security rules stay the same: permission filters are always added, they can never be removed by the caller, and only the fields asked for are fetched. This family is the **newest** and the wiki is deliberately cautious: much of it is *implemented* but not yet *proven by a real database run*.

## Which provider is which?

| | **EF Core provider** | **JPA provider** |
|---|---|---|
| Language | .NET (C#) | Java |
| Your existing tool | Entity Framework Core (`DbContext`) | Jakarta Persistence (`EntityManager`) |
| Main classes | `EfCoreProvider`, `EfCoreQuerySource`, `EfCoreMutationSource` | `JpaOrmProvider`, `JpaOrmQuerySource`, `JpaOrmMutationSource`, `JpaPredicates`, `JpaOrmSnapshotPageSession` |
| Shared engine underneath | `Foundgine.Providers.Orm` | `storage/orm` |
| Maturity | Most complete; real SQLite integration tests exist | Partial; see the status table |

**Which should I use instead of the SQL provider?** Choose the ORM provider when your application already owns its entities and database access through EF Core or JPA and you want one data-access stack, your existing connection and transaction handling, and your ORM's database support. Choose the [SQL provider](providers.md) when you want Foundgine to write the SQL itself, with its cost estimator and PostgreSQL batching. Both keep the same permission rules.

## Plain-words walk-through

Say a caller asks: *"show me customers whose orders include SKU A."*

1. Foundgine works out what that means in your model and checks the caller is allowed (the **authorization binding**).
2. It builds a provider-neutral plan (**Execution IR**).
3. The ORM core checks the plan only asks for things this provider can really do. If not, it **stops before touching the database**.
4. It builds the query from **generated, typed pieces** (no guessing at runtime): the permission filter first, then the caller's filter on top.
5. EF Core (or JPA) turns that into SQL and runs it, fetching **only the requested columns**.
6. Child rows (the orders) are fetched in a **batch** using the parents' keys, with the child's own permission filter applied inside that query.
7. Rows come back with **evidence** (which provider, plan fingerprint, which parts were permission-checked), see [Insights, evidence and auditing](insights-evidence-auditing.md).

## Setting it up (.NET)

1. Mark your entity classes with `[FoundgineEntity]`, `[FoundgineField]` and `[FoundgineRelationship]`. The **ORM map generator** (`Foundgine.Providers.Orm.Generator`) writes the typed maps at build time; nothing is discovered by reflection at run time.
2. Build the registry once from your model metadata, the generated maps and the relationship metadata: `OrmRegistry.Create(entities, maps, relationships)`.
3. Register the EF Core provider with your `DbContext` and that registry, then run your normal Foundgine configuration for the model and authorization. The ORM provider never decides permissions itself.

Java uses the JPA provider with `jakarta.persistence` as an optional dependency; see `src/java/foundgine-providers/.../storage/orm/jpa` and `src/java/ORM-JPA-PARITY-PLAN.md` for current wiring. Check exact registration method names in the source for your version.

## Shape of the family

```text
Semantic operation
  -> authorization binding
  -> semantic plan / Execution IR
  -> provider capability gate
  -> typed fragments (generated maps)
  -> EF Core IQueryable   |   JPA Criteria query
  -> database
```

| Layer | C# | Java |
|---|---|---|
| Provider-neutral ORM core | `Foundgine.Providers.Orm` (`OrmPlanCompiler`, `OrmExecutionProvider`, `OrmMutationExecutionProvider`, `OrmRegistry`, `OrmEntityMap`, `OrmFieldBinding`, `OrmRelationshipBinding`, `OrmCursor`, `OrmProviderCapabilities`) | `foundgine-providers/.../storage/orm` (`OrmPlanCompiler`, `OrmExecutionProvider`, `OrmMutationExecutionProvider`, `OrmRegistry`, `OrmEntityMapping`, `OrmPredicate`, `OrmCursor`, `OrmAggregateCursor*`) |
| Concrete adapter | `Foundgine.Providers.EntityFrameworkCore` (`EfCoreProvider`, `EfCoreQuerySource`, `EfCoreMutationSource`) | `storage/orm/jpa` (`JpaOrmProvider`, `JpaOrmQuerySource`, `JpaPredicates`, `JpaOrmMutationSource`, `JpaOrmSnapshotPageSession`) |
| Test doubles | n/a | `InMemoryOrmQuerySource`, `InMemoryOrmMutationSource` |
| Build-time maps | `Foundgine.Providers.Orm.Generator` (`OrmMapGenerator`, Roslyn) | provider-neutral maps today; see [AOT](aot.md) |

The neutral core references **no** ORM package. The .NET ORM track contains EF Core only; the Java track contains JPA only (optional dependency isolated to `jakarta.persistence`).

## Rules every ORM provider keeps

1. **Authorization is mandatory.** Root authorization is a separate predicate that cannot be removed; child authorization runs inside the child query, not after loading. Missing context fails closed. Caller filters only narrow.
2. **Projection is explicit.** Only requested fields plus private execution fields (keys, correlation keys, cursor slots), which never leak into public results.
3. **Values are parameters.** Never concatenated into JPQL, SQL or expressions.
4. **Capabilities are evidence-gated.** `OrmProviderCapabilities` / `OrmProviderFeature` (scan, filtering, projection, ordering, offset and cursor paging, authorization predicates, relationship filters, aggregate filters, aggregate ordering, per-parent paging) is a *translation* declaration, not a security or runtime certificate. Unsupported shapes fail before touching the database.
5. **Deterministic order.** Pageable orders end in a unique key tiebreaker.
6. **Every page re-authorises.** Cursors are typed, versioned and bound to the plan.
7. **Mutations are atomic.** Validate the whole batch first, one transaction boundary, roll back on failure, never silently commit a caller's transaction.
8. **No accidental graph walking.** No lazy loads or N+1; relationships use planned joins/subqueries or batched child-key fetches.

## Honest status

| Capability | .NET (EF Core) | Java (JPA) |
|---|---|---|
| Scalar filters, projection, root authorization | implemented; translation and runtime evidence *test-defined, not run* in the matrix | partial Criteria translation; previously type-checked against API stubs, not a database |
| Relationship traversal (batched child keys) | implemented | existing child-fetch architecture |
| Relationship filters (some/none/all) | implemented | unsupported |
| Aggregate filters (count/min/max) | implemented | implemented-unverified |
| Aggregate ordering | implemented, opt-in (count) | implemented-unverified for non-paged one-hop orders |
| Aggregate cursor pagination | **disabled** until a database test proves mixed keyset boundaries | typed decode and branch foundation; execution continues to be unverified |
| Snapshot reads across pages | not claimed | opt-in `JpaOrmSnapshotPageSession`: one `EntityManager` and transaction for a whole multi-page read; caller must acknowledge `REPEATABLE_READ_OR_STRONGER`. JPA cannot set or inspect isolation portably, so this is an assertion. H2 and PostgreSQL tests are defined; **not yet run** |
| Atomic mutation batches | one transaction, rollback | basic resource-local adapter |
| Native AOT / Native Image | CI scoped to `linux-x64` and `linux-arm64`; not yet certified by a run | not established |

The machine-readable source of truth for .NET is `src/csharp/conformance/orm/provider-conformance-matrix.json`. It separates implementation, translation, runtime, Native AOT publish and native execution, and states the policy: *a capability is certified only by executed, provider-specific evidence; source presence, expression construction and a configured CI job are not passing evidence.* For Java, see `src/java/ORM-JPA-PARITY-PLAN.md` (workstreams JPA-0 to JPA-9).

Two consistency notes the docs repeat on purpose: the default per-fetch path is live, read-committed-style data (not a snapshot), and a cursor fingerprint alone is not a snapshot guarantee.

## How "some / none / all" treat permissions

Relationship filters ask a question about a customer's orders. The key idea: **orders the caller may not see do not exist, as far as the question is concerned.**

| Filter | Plain meaning | How it is built |
|---|---|---|
| **some** | At least one *visible* order matches | any( visible **and** matches ) |
| **none** | No *visible* order matches | not any( visible **and** matches ) |
| **all** | Every *visible* order matches (a customer with no visible orders passes) | not any( visible **and** does **not** match ) |

The permission test always sits **outside** the negation. Negating "visible and matches" would wrongly treat a hidden order as a failure, so a customer whose only order is hidden from the caller would be wrongly excluded from **all**.

**Result shape.** Rows come back as flat "left join" rows, one per visible child order: a customer with two visible orders appears twice, one with none appears once. De-duplicate if you only want customers.

## Insights for ORM providers

- Evidence is produced for every execution (`ExecutionEvidence`): provider name, plan fingerprint, permission-checked nodes, rows, time. The fingerprint covers the plan *shape*, not the filter values.
- The queries your ORM sends can be inspected with your ORM's normal logging or interceptors.
- Whether a feature is *proven* is tracked in `provider-conformance-matrix.json` and the CI evidence lanes, see [Conformance and hardening](conformance-hardening.md).

## Verifying

- .NET: `dotnet test src/csharp/tests/Foundgine.Orm.Tests/Foundgine.Orm.Tests.csproj -c Release`; Native AOT steps in `src/csharp/aot/README.md`.
- Java: ORM tests plus the opt-in PostgreSQL lane (`FOUNDGINE_TEST_PG_URL`); the Java parent targets Java release 27, so the build must use that toolchain before a Maven result is valid.
- Required layers: pure semantic tests, ORM-implementation tests, database integration tests, cross-runtime parity (same fixtures, compare semantic results not SQL text), Native Image tests, security regressions (unauthorised root/child, forged or replayed cursor, unfiltered mutation, hidden-relationship side channels).

## Where to look

C#: `Foundgine.Providers.Orm`, `Foundgine.Providers.EntityFrameworkCore`, `tests/Foundgine.Orm.Tests`, `conformance/orm`. Java: `foundgine-providers/.../storage/orm` and `.../orm/jpa`.

## Related pages

- [Provider catalogue](providers.md)
- [Providers and storage](providers-storage.md)
- [Insights, evidence and auditing](insights-evidence-auditing.md)
- [Conformance and hardening](conformance-hardening.md)
- [Glossary](glossary.md)
