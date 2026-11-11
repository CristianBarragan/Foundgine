# Foundgine 3.3.1 (.NET 10 and Java 27)

Patch release on the 3.3.x line. .NET and Java ship with the same number.

> **Status: prepared, not yet built.** Every code change in this release was written without a .NET SDK, a Java
> compiler or package access, and no tests were run on the new code. The release gates at the bottom must pass before
> the tag is created. Anything not marked verified below is **implemented-unverified**.

## Added

- **`SemanticQuery` fluent read builder, identical in both runtimes.**
  C#: `Foundgine.Core.Semantic.IR.SemanticQuery`. Java: `com.foundgine.core.semantic.ir.SemanticQuery`.
  Members (PascalCase in C#, camelCase in Java): `From`, `Where`, `WhereEq`, `WhereNeq`, `WhereIn`, `OrderBy`, `Select`,
  `Take`, `Skip`, `Include` (optional child lambda), `Build`. Fields and relationship targets are inferred from the
  metadata; fields can be given by id or by case-insensitive name. The builder only assembles a `SemanticOperation`, so
  authorization and planning are unchanged.
- **EF Core reader conveniences** (`FoundgineEfReader`): `ReadAsync(SemanticQuery)`, `From(entity)`, the lambda form
  `ReadAsync(entity, q => ...)`, and `FoundgineReadResult.FirstOrThrow(message)`.
- **Java plan helper:** `planRead(caller, entity, q -> ...)` on the supply-chain samples (the counterpart of the C#
  lambda read); the older `planRead` overloads and `planWarehouseInventory` now delegate to it.
- **Cross-language conformance:** `src/conformance/semantic-query-vectors.json` (spec `semantic-query-v1`, 9 cases) with
  an independent Python generator, `SemanticQueryConformanceTests` (C#) and `SemanticQueryConformanceTest` (Java).
- **Documentation:** new wiki page *Fluent semantic reads (`SemanticQuery`)* with C# and Java snippets side by side;
  updated ORM providers, conformance, public API, status and index pages.

## Changed

- Both samples read through the builder (C# `GetOrder`; Java `planRead` and `planWarehouseInventory`) instead of
  hand-built read nodes.
- Version labels moved from 3.3.0 to 3.3.1 across the Maven reactor, package READMEs, tutorials, `mcp.json`,
  `llms*.txt` and the documentation site.

## Fixed

- **EF model creation failed for metadata without a column catalog.** `FluentEfModel.Apply` required every field's
  column to be listed on the entity and threw "Sequence contains no matching element" otherwise, which failed three
  `Foundgine.Orm.Tests` (`FoundgineEntitiesTests`). A missing column now falls back to the field name and a missing key
  field skips `HasKey`.

## Also in this tree (confirm against what 3.3.0 shipped)

The EF/JPA samples work from the earlier checkpoints is part of this source tree: the EF provider surface
(`AddFoundgineEf`, `IFoundgineRepository<T>`, `FoundgineEfReader`, `IdentityEntityMap<T>`), the simple and advanced
samples on EF Core, the JPA advanced reads and policy, and the Java `JpaEntities` gateway. If any of it was not in the
3.3.0 artifacts, list it under *Added*. All of it is implemented-unverified except what the passing parts of
`Foundgine.Orm.Tests` already cover (75 of 78 passed before the fix above).

## Known limitations

- No predicate-lambda `Where` (`o => o.Id == x`) in either language; filters use field handles and `Eq/Neq/In`.
- Java has no reader class, so there is no Java `ReadAsync` counterpart yet.
- `JpaEntities` is source and unit test only; not compiled against Hibernate/H2.
- Aggregate cursor pagination stays disabled until a database test proves it.
- Benchmark and performance pages are unchanged and were measured on earlier builds.

## Release gates (all must pass before tagging `v3.3.1`)

1. `dotnet build` and `dotnet test` on the whole solution, including `Foundgine.Orm.Tests` and
   `dotnet test --filter SemanticQueryConformance`.
2. Java 27 toolchain: `mvn -pl foundgine-core test -Dtest=SemanticQueryConformanceTest`, then the full reactor and
   `JpaEntitiesTest`.
3. **`Directory.Build.props` must say 3.3.1.** It is the canonical .NET version source and the build workflow rejects a
   tag that differs; the workflow's required-version value must be updated too. Neither file was part of the files used
   to prepare this release, so they are **not yet bumped**.
4. Regenerate the Maven `.flattened-pom.xml` files and the website HTML / `llms-full.md` from the Markdown sources.
5. Add the block below to `CHANGELOG.md`, tag `v3.3.1`, publish NuGet and Maven Central artifacts.

## `CHANGELOG.md` entry (paste at the top)

```markdown
## 3.3.1

### Added
- `SemanticQuery` fluent read builder with identical members in C# and Java; EF reader overloads (`ReadAsync(query)`,
  lambda form, `FirstOrThrow`); Java `planRead(caller, entity, lambda)`.
- Shared conformance vectors `semantic-query-vectors.json` with C# and Java tests.
- Documentation page "Fluent semantic reads" with C# and Java snippets.

### Fixed
- `FluentEfModel.Apply` no longer throws for metadata whose fields reference columns the entity does not list.

### Notes
- Implemented-unverified: the new code was written without a compiler; see RELEASE-NOTES-3.3.1.md for the gates.
```
