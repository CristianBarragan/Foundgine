# ORM providers (EF Core and JPA)

> **In plain words.** Many teams already have an ORM. Instead of making them hand-write SQL, Foundgine can hand its plans to **EF Core** (.NET) or **JPA** (Java). The ORM turns the plan into database queries. The security rules stay the same: permission filters are always added, they can never be removed by the caller, and only the fields asked for are fetched. This family is the **newest** and the wiki is deliberately cautious: much of it is *implemented* but not yet *proven by a real database run*.

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

## Verifying

- .NET: `dotnet test src/csharp/tests/Foundgine.Orm.Tests/Foundgine.Orm.Tests.csproj -c Release`; Native AOT steps in `src/csharp/aot/README.md`.
- Java: ORM tests plus the opt-in PostgreSQL lane (`FOUNDGINE_TEST_PG_URL`); the Java parent targets Java release 27, so the build must use that toolchain before a Maven result is valid.
- Required layers: pure semantic tests, ORM-implementation tests, database integration tests, cross-runtime parity (same fixtures, compare semantic results not SQL text), Native Image tests, security regressions (unauthorised root/child, forged or replayed cursor, unfiltered mutation, hidden-relationship side channels).

## Where to look

C#: `Foundgine.Providers.Orm`, `Foundgine.Providers.EntityFrameworkCore`, `tests/Foundgine.Orm.Tests`, `conformance/orm`. Java: `foundgine-providers/.../storage/orm` and `.../orm/jpa`.
