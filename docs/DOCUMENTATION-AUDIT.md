# Documentation and source audit

**Input snapshot:** supplied `wiki (1).zip`, `FoundgineDocs (2).zip`, and `src-fixed.zip`  
**Repository version:** `2.3.1`  
**Audit date:** 2026-11-11

## What changed

- Integrated all 20 supplied feature/capability wiki pages into `docs/wiki/`.
- Added the wiki to the MkDocs navigation and linked the feature pages from the documentation landing page.
- Added `SOURCE-COVERAGE.md`, including the product module map, provider/extension coverage, all project-file paths and source inventory totals.
- Updated current status, architecture, public API, AI/search reference and LLM reference documents against the supplied source.
- Updated root README package references and removed local links to website/standards artifacts that were not present in the supplied bundle.
- Merged the supplied `src/` tree into this repository snapshot so source references resolve and the delivered archive contains both documentation and implementation.

## Source-derived corrections

1. .NET project target is `net10.0`.
2. Java Maven compiler release is `27`.
3. Elasticsearch semantic lexical retrieval and PostgreSQL `pgvector` lexical retrieval exist in both source trees.
4. GraphQL exists in both source trees, using Hot Chocolate in .NET and GraphQL Java in Java; these are different framework adapters.
5. ORM support is split into the .NET provider-neutral ORM core, EF Core adapter and ORM map generator, and the Java provider-neutral ORM core and JPA adapter. Verification is capability-specific.
6. Java annotation processor sources are packaged in `foundgine-providers` and consumed from the reactor. A separate `foundgine-aot-generator` directory exists but is not listed in the root Maven reactor.
7. The public package map now lists actual project/package IDs instead of presenting internal namespaces as separate packages.

## Validation performed

| Check | Result |
|---|---|
| Supplied wiki pages copied | 20 |
| MkDocs navigation targets | 40; all resolve |
| Local Markdown links | 0 broken |
| .NET project files inventoried | 70 |
| Java Maven POM files inventoried | 10 |
| Source tree merged | Yes (`src/`) |
| MkDocs YAML syntax | Parsed successfully |

## Not claimed

This was a documentation/source-structure audit. It did **not** execute .NET builds, Maven builds, database integration tests, Native AOT publishes, GraalVM Native Image runs, or live provider conformance suites. The documentation intentionally preserves implemented/unverified/test-defined/certified distinctions rather than inferring runtime proof from source presence.
