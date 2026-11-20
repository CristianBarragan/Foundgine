# Documentation audit — checkpoint report

## Scope

This report records a source-tree audit of the documentation-alignment archive. It covers local Markdown/HTML links, README and AI-context consistency, navigation metadata, and selected integration/package claims checked against project files. It is **not** a full language build, test run, deployment check, external-link check, or complete claim-by-claim implementation audit.

## Findings and corrections in this checkpoint

| Finding | Evidence | Correction |
|---|---|---|
| C# sample index had an invalid relative path to the provider samples README. | The target is under `src/csharp/Foundgine.Providers/Samples`, not `src/Foundgine.Providers/Samples`. | Corrected `src/csharp/samples/README.md`. |
| Java sample index had an invalid relative path to the C# samples README. | Relative path resolved inside `src/java/csharp/`, which does not exist. | Corrected `src/java/samples/README.md`. |
| Framework integration package wording was ambiguous. | C# source adapters are non-packable projects; the packaging project bundles their assemblies into `Foundgine.Providers`. Java provider sources contain Spring/LangChain4j integrations, while separate module directories exist but are not in the parent Maven reactor. | Updated `docs/wiki/agent-framework-integrations.md` and `llms-full.md` to distinguish source projects, bundled package/module, reactor inclusion, and publication verification. |
| Root and website `llms-full.md` had substantive differences. | Diff showed changed lifecycle vocabulary, omissions, different implementation-status caveats and duplicate/contradictory integration sections. | Website full-context file now follows root canonical technical context, with site-specific title and navigation. |
| MkDocs description did not use the updated product positioning. | `mkdocs.yml` described only a “semantic execution layer”. | Updated description to the configurable semantic/execution boundary framing. |
| Verification documentation contradicted the solution file. | `Foundgine.Providers.AgentFramework` and its test project are present in `Foundgine.sln`, while `docs/SOURCE-COVERAGE.md` and `docs/wiki/implementation-map.md` said they were excluded. | Corrected those statements; successful build/test status remains unverified. |
| Release notes contain conflicting package-layout statements. | The 3.4.0 changelog describes restructuring optional projects, then later states that Redis/MAF and Java framework adapters were consolidated into the provider packages; the later consolidation entry supersedes the earlier layout description. | AI/full-context docs now describe bundled assemblies/source projects and distinguish standalone Java module directories from the parent reactor.

## Link check

Run:

```bash
python tools/doc-audit-links.py
```

The checker resolves ordinary relative Markdown links, MkDocs extensionless pages, local HTML links and site-root links. It does not verify external HTTP links, anchors, generated pages, or links inside arbitrary code blocks.

## Verification limits

- `mkdocs`, `dotnet` and `mvn` were unavailable in the audit environment, so strict MkDocs build, C# build/tests and Maven verification were not run.
- Source and test files are evidence of implementation presence only; they do not establish successful build, runtime behavior, CI status or registry publication.
- The website is a static directory in this archive. No deployment or live-site verification was performed.
- External URLs, package registry status, badge endpoints, and release artifacts remain to be checked.

## Remaining audit work

1. Validate the full MkDocs build and navigation.
2. Verify C# and Java API snippets against current samples and public APIs.
3. Audit provider and extension capability/maturity claims, including EF Core, JPA, Redis, Temporal, Keycloak, GraphQL, MCP, retrieval and SDKs.
4. Audit security statements against implementation, tests and documented host responsibilities.
5. Check external links, website deployment paths, sitemap and metadata.
6. Reconcile current implementation status with changelog, CI and release/package artifacts.
7. Remove or clearly label duplicated historical/checkpoint documents that are not current operational guidance.
