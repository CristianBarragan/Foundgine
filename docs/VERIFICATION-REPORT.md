# Documentation verification report

Scope: docs vs source for C#, Java, TypeScript, Python and WASM. Version audited: 3.3.5.

## Verified (matches source)
- Version `3.3.5` in `Directory.Build.props`, Java `<revision>`, `@foundgine/typescript`, `foundgine-sdk` (Python), `@foundgine/wasm`, WASM `Cargo.toml`.
- Java artifactIds in `docs-site/packages` (`foundgine-runtime`, `-providers`, `-extensions`, `-migrations`) exist as modules.
- C# install commands name real projects (`Foundgine.Runtime`, `Foundgine.Providers`).
- All six CI workflows referenced by README badges exist.
- TypeScript, Python and WASM README examples: documented classes and methods (`FoundgineClient`, `FoundgineSyncClient`, `listCapabilities`/`list_capabilities`, `execute`, `init`) exist in source.
- Backticked type names across docs (provider, gateway, security, MCP, approval pages): all resolve in source except the fixes below.
- All GitHub `blob/main` and `tree/main` links point at paths that exist.

## Verified in checkpoint 3 (security, MCP, website, release)
- `SecurityResourceLimits` table in `SECURITY.md`: all 18 defaults match C# (`SecurityResourceLimits.cs`) and Java (canonical constructor defaults).
- `EncryptingFoundgineGatewayApprovalPayloadCodec` exists in C# and Java; `IAuditLog.Record` is synchronous; `InMemoryAuditLog` exists in both.
- `policy_probe` behaviour in `SECURITY.md` matches the sample: verbose detail only when `IsDevelopment()`, otherwise logs server-side and returns `{ allowed: false, reference }`. MCP tool names in docs exist in source.
- README badge jobs (8) all exist in the referenced workflows.
- `docs-site` relative links resolve; no stale versions, `TODO`/`TBD` markers or old `foundgine-sdk/` SDK paths in prose.
- The Microsoft Agent Framework adapter (`AddFoundgineAgentFramework`, `ApprovalRequiredAIFunction`, `FoundgineProviderKind.Agent`/`AGENT`) is now compiled into `Foundgine.Providers`; the supplied archive has not been built in this environment, so package/runtime verification is still pending.

## Added in checkpoint 4
- `wiki/mcp-protocol-and-tokens.md`: protocol versions, sessions/SSE and `fgmcp1` approval tokens, written from source.

## Added in checkpoint 5
- 16 `.puml` files recovered from SVG-embedded source (7 in `docs/diagrams`, 9 in `docs/assets`); 4 more added in checkpoint 7. None re-rendered (no PlantUML here).

## Diagram hygiene (checkpoint 6)
- `docs/diagrams/` is an unreferenced duplicate of `docs/assets/`; pages embed from `docs/assets/`. 9 of 10 SVGs are identical; `lexical-grounding-flow.svg` differs (older, no notes).
- `docs/assets/pgvector-e2e-pipeline.svg` was unreferenced; it is now embedded in `POSTGRES-E2E.md` (checkpoint 8).
- The 83 numbered SVGs are all used by pages; their PlantUML originals would need to come from the author's history.
- Decision for the owner: delete `docs/diagrams/` or generate it from `docs/assets/`. Nothing was deleted.

## Fixed
- `EfCoreProvider` -> `EfCoreExecutionProvider` (`wiki/orm-providers.md`).
- `InMemoryProvider` -> `InMemoryExecutionProvider` (`wiki/providers.md`).
- 31 links to files outside `docs/` rewritten to GitHub URLs; 3 extensionless wiki links fixed.
- MkDocs `--strict` build passes.

## Open / not verifiable here
- External links (67, mostly github.com, shields.io, npm, nuget, Maven Central, GitHub Pages) not checked: network is disabled in this environment.
- `docs/SECURITY.md` cites `McpException` (external MCP SDK type) and `BlockedCallClassification` (sample code only); both are sample-level, not library API.
- Diagram sources: 20 `.puml` files recovered in total (11 in `docs/assets`, 9 in `docs/diagrams`; see `docs/diagrams/README.md`). `lexical-grounding-alias-weight.svg` is hand-written SVG. 83 numbered `*-plantuml-NN.svg` files are converted standalone SVGs whose original PlantUML is not in the repo.
- Prose claims about threat-model behaviour not backed by a named identifier or number were not individually tested.
- Generated SVGs embed old PlantUML/Graphviz versions in comments; harmless, but they confirm the diagrams are not regenerated from source here.
