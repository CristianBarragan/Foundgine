# Changelog

## 3.3.1 — November 11, 2026

### Changed

- Aligned the canonical Foundgine .NET and Java release versions on `3.3.1`
  (`VersionPrefix` in `Directory.Build.props` and `<revision>` in
  `src/java/pom.xml`).

### CI

- `build.yml` and `java-build.yml` no longer hardcode the canonical version.
  `build.yml` reads `VersionPrefix` from `Directory.Build.props` and
  `java-build.yml` reads `<revision>` from `src/java/pom.xml`. A release tag
  must equal that value, and non-tag builds are versioned
  `<canonical>-ci.<run-number>`. This fixes release tags such as `v3.3.1`
  failing with `Release tag … does not match canonical version 3.3.0`, and
  means the next release only needs the two version files bumped.

### Java build

- The root `pom.xml` now activates `maven-source-plugin` and
  `maven-javadoc-plugin` (with `doclint` disabled), so every published module
  produces the `-sources` and `-javadoc` jars Maven Central requires. They were
  previously declared in `pluginManagement` only and never executed.
- `foundgine-aot-generator` is excluded from the Central Portal deployment,
  matching its documented role as a reactor-only annotation processor.

### Providers

- **Java JPA provider (`storage.orm.jpa`).** Hibernate ORM 7.4.15.Final with H2
  2.5.252 and a PostgreSQL persistence unit are now test-scoped dependencies,
  with SQL statement capture, Criteria tuple-query integration tests,
  `EntityManager` ownership checks and owned/caller-owned transaction coverage.
  New or extended capabilities, all implemented-unverified until the Java 27
  H2/PostgreSQL workflow passes:
  - Scalar translation: typed, per-execution named `ParameterExpression`
    binding for comparisons and every `IN` member; explicit empty and NULL-only
    `IN` handling; mapped-type checks; an ordering allowlist (boxed numerics,
    `BigDecimal`/`BigInteger`, `String`, supported `java.time` types) that
    rejects enum and UUID ordering.
  - Relationship predicates: correlated `EXISTS` and `SOME`/`NONE`/`ALL`
    quantifiers over the authorization-visible child set (ALL is vacuously true
    when no authorized child exists), bounded by `OrmPredicateValidator`
    (256 nodes, nesting depth 8, cycle detection).
  - Authorized relationship aggregates (`COUNT`, `MIN`, `MAX`) as filters, and
    non-paged one-hop aggregate ordering.
  - Aggregate cursor groundwork and execution integration: canonical typed
    cursor values, plan-bound cursor envelope with a SHA-256 fingerprint,
    private aggregate tuple transport, and `JpaAggregateContinuation`
    lexicographic continuation predicates.
  - Opt-in `JpaOrmSnapshotPageSession`: one `EntityManager` and transaction for
    a whole multi-page read. The caller must acknowledge
    `REPEATABLE_READ_OR_STRONGER`, because JPA cannot set or inspect
    isolation portably. H2 and PostgreSQL concurrency tests are defined.
- **.NET EF Core provider.** `Foundgine.Providers.Orm` and
  `Foundgine.Providers.EntityFrameworkCore` carry the explicit, versioned
  `OrmProviderCapabilities` declaration (relationship filters, aggregate
  filters, aggregate ordering, per-parent pagination as separate features), and
  `FluentEfModel.Apply` maps fluent Foundgine metadata onto an EF Core model
  (table, key and column names from the metadata; navigation members ignored).
  Aggregate cursor pagination stays disabled until translation and runtime
  tests pass.
- **Conformance evidence.** The .NET matrix
  (`src/csharp/conformance/orm/provider-conformance-matrix.json`) and the Java
  plan (`src/java/ORM-JPA-PARITY-PLAN.md` and
  `conformance/orm-jpa-parity-plan.json`) record translation, runtime and
  Native AOT status per capability. Source presence or a configured CI job is
  not counted as passing evidence.

### Samples

- **.NET Supply Chain starter** (`src/csharp/samples/Foundgine.SupplyChain`)
  runs on EF Core with `Npgsql.EntityFrameworkCore.PostgreSQL`, referencing
  `Foundgine.Providers.Orm` and `Foundgine.Providers.EntityFrameworkCore`.
- **Java Supply Chain starter**
  (`src/java/samples/foundgine-supply-chain`) now has a JPA + PostgreSQL read
  path (`SupplyChainJpa`, `SupplyChainSemantics`, `SupplyChainPolicy`) mapping
  `Supplier`, `Warehouse` and `Product`; `SupplyChainSemanticsTest` exercises
  the semantic, authorization and planning half against the in-memory ORM
  source. The Hibernate/PostgreSQL half has not been compiled or run, and the
  MCP transport and mutation path are not wired in this starter.
- Java advanced sample gains `AdvancedJpaSemanticsTest`.
- Starter tutorials and README files updated for the new provider wiring and
  version.
  
### Documentation

- Corrected the ORM provider status table in `docs/wiki/orm-providers.md`:
  Java relationship filters (`SOME`/`NONE`/`ALL`) and aggregate filters were
  listed as unsupported or unqualified, but the JPA predicate translation
  exists and is implemented-unverified. Semantic filter lowering to those
  predicates is not wired, so the shapes still fail closed end to end. The
  .NET cells now state that translation and runtime evidence is
  test-defined, not run, and Java aggregate ordering now lists count, min and
  max.
- Fixed a stale bullet in the Java ORM README
  (`storage/orm/README.md`) that listed relationship quantifiers, aggregate
  filters and aggregate ordering as not implemented, contradicting its own
  step sections.

### Release

- Version: `3.3.1`
- Target framework: `.NET 10` / Java 27
- License: Apache License, Version 2.0

## 3.3.0 — November 11, 2026

### Changed

- Aligned the canonical Foundgine .NET and Java release versions on `3.3.0`.
- Updated NuGet package metadata, Maven reactor and flattened consumer POMs,
  CI package versioning, MCP manifests, installation examples, public
  documentation, website metadata and AI reference files to `3.3.0`.
- Preserved third-party dependency versions; Foundgine release numbering is
  independent of the versions of .NET, Java, xUnit, Model Context Protocol,
  Maven plugins and other external dependencies.
- Fixed Docker metrics summarization under PowerShell strict mode by
  normalizing collector errors to an array and restoring the zero-sample
  error-handling branch.

### Release

- Version: `3.3.0`
- Target framework: `.NET 10` / Java 21
- License: Apache License, Version 2.0

## 2.3.1 — November 9, 2026

### Changed

- Aligned the canonical .NET and Java release versions on `2.3.1`.
- Updated the checked-in Maven consumer POMs, MCP discovery manifests, package
  installation examples, public documentation, website metadata and AI
  reference files to identify `2.3.1` as the current release.
- Aligned the .NET CI build's non-tag package version with the `2.3.1` release
  line (`2.3.1-ci.<run-number>`).
- Removed stale per-package `2.0.0` release-note overrides so all NuGet
  packages inherit the shared changelog metadata.
- Corrected three broken project-reference paths in the Run1 benchmark project.
- Removed a tracked, machine-generated TRX test report that contained stale local
  .NET 9 build paths; retained the human-readable and JSON benchmark reports.

### Release

- Version: `2.3.1`
- Target framework: `.NET 10` / Java 21
- License: Apache License, Version 2.0

## 2.3.0 — November 8, 2026

### Added

- **Fluent configuration API for .NET and Java.** A second, code-first way to
  produce the same `MetadataRegistry` that the `[Foundgine*]` attributes /
  annotations and the AOT generator produce. Each configuration class owns one
  concern — semantic entity and relationships, persistence (table, key, columns,
  indexes), model-member-to-storage-member maps, relationship-to-foreign-key
  connections, and model-to-entity connections — and a single `FoundgineModel`
  subclass registers them. `Build()` validates the whole set and reports every
  problem in one exception; `BuildInto(registry)` adds the result to an existing
  registry and refuses to overwrite ids that are already registered.
  - .NET: `Foundgine.Core.Semantic.Configuration` (`FoundgineModel`,
    `FoundgineModelConfiguration<T>`, `FoundginePersistenceConfiguration<T>`,
    `FoundgineEntityMap<TModel, TEntity>`, `FoundgineConnectionConfiguration`,
    `FoundgineModelConnectionConfiguration<T>`).
  - Java: `com.foundgine.core.semantic.configuration` with the same shape
    (`FoundgineModel.build()` / `buildInto(...)`).
- **Model connections with conversions and authorization.** `HasConnection` /
  `hasConnection` maps a model member to a storage entity; `Maps(source, target,
  convert)` records a `ConversionMetadata` for the type pair, and `Authorize` /
  `authorize` reads the predicate as an expression (never executed at run
  time) into an `AuthorizationPredicate`.
- `ColumnMetadata.MaxLength` and `ColumnMetadata.IsRequired` (.NET), populated by
  `HasMaxLength` / `IsRequired`.
- Samples now configured fluently: the .NET Supply Chain starter
  (`src/csharp/samples/Foundgine.SupplyChain`, `Domain/SupplyChainModel.cs`) and
  the .NET and Java Supply Chain advanced samples. Application code uses the
  `SupplyChainSemantics` handles, which are checked against the compiled registry
  at first use.

### Changed

- **Target framework is now .NET 10.** Every C# project under `src/csharp`
  already built for `net10.0`, while the repo defaults, CI and docs still said
  .NET 9. `Directory.Build.props` now defaults to `net10.0`, the `build.yml` and
  `security.yml` workflows install the `10.0.x` SDK, and every doc, website page,
  `llms*` file and `mcp.json` reads ".NET 10". Java stays on 21.

### Documentation

- Aligned the whole repository and website on `2.3.0`: `Directory.Build.props`,
  `README.md`, `ai.seo.md`, `llms.txt`, `llms-full.md`, `docs/`, `docs-site/`
  (install snippets, hero badge and every page footer) and the Java reactor
  (`<revision>` and the flattened POMs).
- `mcp.json` and `.well-known/mcp.json` had drifted to `2.0.3` and now read
  `2.3.0`; `.well-known/mcp.json` also pointed `mcpProject` at a path that does
  not exist (`src/Foundgine.Providers/...`) and now matches `mcp.json`.
- Documented the fluent API in `docs/MAPPING.md`, `docs/PUBLIC-API.md`,
  `docs/ARCHITECTURE.md`, `docs/GETTING-STARTED.md`, `docs/CURRENT-STATUS.md`
  and the website's Get started page.
- Rewrote the sample tutorials for the fluent API: the .NET starter
  tutorial and "explained" guide, and the Java starter tutorial, advanced
  tutorial and "explained" guide. They previously walked through attribute
  declarations and generated `GeneratedSemanticModel` / `GeneratedMetadata`
  types that the samples no longer use.

### Known limits

- Composite primary keys and composite foreign keys are not supported by the
  fluent API (or by Foundgine metadata); `Build()` reports them.
- Type conversions are available on model connections only; entity maps still
  require identical CLR/Java types.
- Convention-based connection fields are not inferred; list each `Maps(...)`.
- Discovery of `FoundgineModel` subclasses by the source generator is not
  implemented; `Build()` runs at startup.

### Release

- Version: `2.3.0`
- Target framework: `.NET 10` / Java 21
- License: Apache License, Version 2.0

## 2.2.1 — September 14, 2026

### Documentation

- Fixed version drift across public docs and the website: several pages
  (`ai.seo.md`, `docs/CURRENT-STATUS.md`, `docs/README.md`, `docs/PUBLIC-API.md`,
  `llms.txt`, `llms-full.md`, `docs-site/llms.txt`, `docs-site/llms-full.md`,
  and 21 `docs-site/**/index.html` page footers) still described the .NET line
  as `2.0.1` and/or the Java line as `2.2.0`, even though 2.2.0 already
  aligned the two ecosystems onto a single shared version number. The
  `docs-site` install snippet (`dotnet add package Foundgine.Runtime/Providers
--version 2.0.1`) and the homepage hero badge (`v2.0.1`) had the same
  problem. All now read `2.2.1` consistently.

### CI

- Fixed `.github/workflows/java-build.yml`'s `java-security-penetration` job,
  which called `mvn -pl security/pentest -am verify` — `security/pentest`
  has never been a Maven module (it's the live-scanner script directory, no
  `pom.xml`), so the job failed outright. It now runs
  `security/pentest/run-all.sh` with `SKIP_LIVE=true`.
- Fixed the same underlying bug inside `run-all.sh` itself, plus a second,
  previously-latent one: `ROOT` only climbed one directory from
  `security/pentest`, landing in `security/` (no `pom.xml`) instead of
  `src/java`, which also broke the script's live-check paths
  (`run-nmap.sh`/`run-zap.sh`) whenever `SKIP_LIVE` was unset.
- `publish-nuget` (`build.yml`) previously did not wait on
  `supplychain-sample-tests`, `supplychain-semantic-sample-tests`, or any
  `security.yml` job before publishing to NuGet — the latter was structurally
  impossible, since `needs:` cannot reference a job in a different top-level
  workflow. `security.yml` now also triggers via `workflow_call` (its
  `schedule`/`workflow_dispatch` triggers are unchanged), `build.yml` calls it
  through a new `security-gate` job, and `publish-nuget` now depends on all of
  the above plus `security-gate`.
- `publish-maven` (`java-build.yml`) previously did not wait on
  `java-security-penetration` before publishing to Maven Central. It now does.

### Release

- Version: `2.2.1`
- Target framework: `.NET 9` / Java 21
- License: Apache License, Version 2.0

## 2.2.0 — September 14, 2026

### Security

- Resolved the open question in `docs/SECURITY.md` ("the semantic API's
  response verbosity"): `SupplyChainMcpTools.PolicyProbe` (the sample
  `policy_probe` MCP tool under `src/csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Api/Mcp`)
  now only returns its full `kind`/predicate/named-claim decision detail
  when the host is running in a development environment
  (`IHostEnvironment.IsDevelopment()`). In every other environment it logs
  the full decision server-side via `ILogger`, keyed by a short opaque
  correlation id, and returns only `{ allowed: false, reference: <id> }` —
  matching the correlation-id pattern `MCP.Foundgine/Program.cs`'s `Execute`
  helper already used for the execution API, so a caller probing many
  `attack` variants can no longer use the response shape to map policy
  boundaries.

### Release

- The C# NuGet packages and Java Maven artifacts now ship as aligned,
  same-numbered releases: `Foundgine.Core`/`foundgine-core`,
  `Foundgine.Runtime`/`foundgine-runtime`,
  `Foundgine.Providers`/`foundgine-providers`, and
  `Foundgine.Extensions`/`foundgine-extensions`.
- Corrected the NuGet package metadata's license expression from `MIT` to
  `Apache-2.0`. The packaged `LICENSE` file was always the Apache License,
  2.0 text; only the `PackageLicenseExpression` in `Directory.Build.props`
  disagreed with it. Added an equivalent `<licenses>` declaration to the
  Java `src/java/pom.xml` parent so both ecosystems advertise the same
  license.
- The Java build previously hardcoded `2.0.0-SNAPSHOT` as a literal string
  in ten separate `pom.xml` files (the parent's own `<version>`, every
  module's `<parent>` reference, and most intra-repo `<dependency>`
  versions), even though a `<revision>` property already existed and was
  already used correctly in a few places (e.g. the sample projects'
  annotation-processor path). That property was otherwise dead — nothing
  read it. `src/java/pom.xml` now sets `<version>${revision}</version>` on
  the parent itself, every module and intra-repo dependency below it
  references `${revision}`, and `flatten-maven-plugin` resolves that
  property into a concrete version in any installed/deployed POM. This
  makes `revision` the single source of truth for the Java release version,
  the equivalent of `VersionPrefix` in `../../Directory.Build.props` for
  the C# tree.
- Added missing `<description>` elements to the `foundgine-runtime` and
  `foundgine-providers` POMs (mirroring `Foundgine.Runtime` and
  `Foundgine.Providers` on the C# side); `foundgine-core` and
  `foundgine-extensions` already had one.
- Version: `2.2.0`
- Target framework: `.NET 9` / Java 21
- License: Apache License, Version 2.0

## 2.1.0 — September 8, 2026

### Samples

- Added a generic, authorization-focused MCP server (`Foundgine.SupplyChain.Advanced.Mcp.Api` under `src/csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Api/Mcp`) exposing `describe_capabilities`, `read_entity`, `read_relationship`, `write_entity`, and `policy_probe` tools directly against the sample's composed semantic model. This is additive: it runs standalone and is not wired into the Supply Chain E2E harness.

### Fixed

- The commit that added the generic MCP server also deleted `src/csharp/samples/Foundgine.SupplyChain.Advanced/MCP.Foundgine` (the Postgres-backed MCP server the Supply Chain E2E `Agent` and `run-supply-chain.ps1` actually depend on — `get_order`, `get_inventory`, `list_suppliers`, and the rest of the Agent's tool contract) along with its `docker-compose.yml` service and solution entries, without updating the harness. This broke `run-supply-chain.ps1` at step 3/8 with `no such service: mcp-foundgine`. Restored the `MCP.Foundgine` project, its `docker-compose.yml` service, and its `Foundgine.sln` entries so the Supply Chain E2E + PenTest harness runs again.
- Reverted an unrelated, uncommitted in-progress edit to `src/csharp/benchmarks/AgentEndToEnd/Run5/docker-compose.yml` that had stripped its `mcp-foundgine` service block while the project files were left in place, which would have broken Run5 the same way.

### Release

- Version: `2.1.0`
- Target framework: `.NET 9`
- License: MIT

## 2.0.3 — September 7, 2026

### Semantic grounding

- Added `CandidateTruncation` diagnostics to `SemanticLexicalResolver`: when a token's retrieved candidate set exceeds `candidateLimit`, the resolver now records how many candidates were kept vs. discarded, the retrieval score at each side of the cut, and the resulting `MarginGap`. Candidates cut at retrieval time never reach graph search, so previously they were simply gone with no record of whether one might have represented a distinct, legal meaning.
- `GroundingDecision` gains `TruncationRisks` (empty, never null, via `EffectiveTruncationRisks`). When the leading interpretation depends on a token whose highest-scoring truncated candidate falls within the resolver's ambiguity margin of the lowest candidate it kept, the outcome is now `RequiresClarification` instead of `Committed` — the same "don't silently pick a winner inside the margin" policy that already governs surviving interpretations, applied one stage earlier to a candidate that never got the chance to survive or fail on its merits.
- This is diagnostic and fail-closed only: it can turn a `Committed` outcome into `RequiresClarification`, but never the reverse, and it does not change grounding for expressions with no truncation.

### Tests

- Added `SemanticLexicalResolverTests` coverage for the new truncation-risk path: within-margin truncation forcing clarification, clear-margin truncation still committing, and the diagnostic being empty when no truncation occurs.

### Release

- Version: `2.0.3`
- Target framework: `.NET 9`
- License: MIT

## 2.0.2 — September 6, 2026

### Semantic grounding

- Fixed `SemanticLexicalResolver.Ground` so the compact-name fallback (e.g. `customer profile` → `customerprofile`, `purchase order` → `purchaseorder`) is actually reached: the initial per-token retrieval pass now stops as soon as any single token comes back with no candidates, instead of continuing to query the remaining tokens first. Continuing to query every token before falling back was spending the shared retrieval-timeout budget on results that were about to be discarded anyway, which could starve the compact-fallback lookup of the time it needed and made it appear as though the fallback simply hadn't run.
- Fixed a latent `KeyNotFoundException` in the "no lexical candidate" diagnostic: if the compact-name fallback itself also returned no candidates, the resolver could try to look up a token that had never been queried (because retrieval stopped early per the fix above) and crash instead of reporting `GroundingOutcome.Unresolved` cleanly.

### Tests

- Widened the timing margins in `Ground_uses_one_retrieval_deadline_across_compact_token_fallback` (both `Foundgine.Semantics.Tests` and the `Foundgine.SupplyChain.Advanced` sample) so the assertions no longer depend on sub-15ms real-time scheduling precision. Windows' default thread-timer resolution can silently round a `Thread.Sleep(1)` up to ~15ms, which was enough on its own to exhaust the previous 10ms retrieval budget before the compact-token lookup even started. The test now uses a `TimeSpan.Zero` sleep for the always-fast lookup and a 200ms budget with a 500ms fallback delay, comfortably clear of any plausible scheduler jitter, while asserting exactly the same behavior.

### Release

- Version: `2.0.2`
- Target framework: `.NET 9`
- License: MIT

## 2.0.1 — September 5, 2026

### Semantic grounding

- Fixed retrieval timeout accounting so one retrieval deadline is shared across all tokens and the compact-name fallback; provider overruns are detected after synchronous retrieval returns, and cancellable providers receive a linked timeout token.
- Corrected `candidateLimit` documentation to describe the implemented total per-token limit across semantic kinds.
- Renamed `GroundingInterpretation.Confidence` to `InterpretationScore` to make its heuristic ranking semantics explicit.
- Clarified that ambiguity is based on score separation within the configured ambiguity margin rather than a literal tie.
- Clarified that alias `Weight` is application-declared evidence weight, not confidence or priority.
- Documented that `CompetingInterpretations` is internal semantic metadata and should be projected safely before exposure to untrusted callers.
- Corrected the advanced Supply Chain weighted-alias test so it asserts only the semantic identity that actually participated in lexical grounding.

- Restored deterministic alias/synonym grounding for generated semantic contracts.
- Canonical semantic identity is independent of caller wording, so a declared alias resolves to the same interpretation as its canonical name.
- Retrieval representations of the same stable semantic identity no longer create false ambiguity during grounding.
- Added a bounded compact-name fallback for multi-word canonical names such as `purchase order` when token-by-token lexical retrieval does not produce a candidate.
- Advanced Supply Chain grounding coverage explicitly exercises the README case:
  - `purchase orders` / `buys` → `PurchaseOrder`
  - `supplier` / `seller` → `Supplier`
- Added optional alias evidence weights (1–100) for entity, field, and relationship aliases, with `AliasWeightEvidenceGate` for thresholded, fail-closed evidence evaluation that never grants authorization.

### Documentation

- Updated the root README to explain the canonical request and its alias-matched paraphrase layer by layer.
- Added a concrete PlantUML source and matching SVG for the alias-matched Supply Chain execution path under `docs/assets/`.
- Documented that alias matching changes vocabulary, not authority: retrieval remains discovery, while authorization remains application-controlled.

### Release

- Version: `2.0.1`
- Target framework: `.NET 9`
- License: MIT

- Java parity: added Runtime plan-approval E2E tests covering successful approved execution, semantic-version tampering, and plan-fingerprint tampering.
