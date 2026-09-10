# Changelog

## Unreleased

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
