# Ambiguity Resolution ("Grounding")

Files: `Tests/Grounding/SupplyChainGroundingAmbiguityTests.cs`,
`Tests/Grounding/SupplyChainGroundingBudgetTests.cs`,
`Tests/Grounding/SupplyChainGroundingUnresolvedTests.cs`.

Core design docs these tests are case studies against (read these for the
general-purpose design; this file only covers what's specific to being
tested against *this* sample's real generated schema):
`docs/LEXICAL-GROUNDING.md` and `docs/GROUNDING-DECISIONS.md` at the repo
root.


> **Retrieval deadline case studies:** `Semantic/Tests/Grounding/SupplyChainGroundingRetrievalDeadlineTests.cs` covers three distinct failure modes: deadline exhausted before a provider call, a provider returning after the deadline, and a compact-token fallback consuming the remaining portion of the same shared deadline.

## The concept: grounding, and why it's a distinct step from planning

"Grounding" is the step where a natural-language phrase from an agent or
user (`"show me our active suppliers"`) gets matched against the semantic
contract — which entity is "suppliers," which field or relationship is
"active" — *before* anything gets planned into a query. `SemanticLexicalResolver.Ground`
is the entry point; its result is a `GroundingOutcome`, one of:

- **Committed** — exactly one interpretation survived, safe to plan.
- **Unresolved** — some token had *no* candidate interpretation at all.
- **BudgetExceeded** — the resolution process was cut off by a complexity
  bound before it could prove there was only one legal interpretation.

The important design decision, stated directly in the doc comments across
this test suite: **there is no fourth outcome that means "picked the
best-scored guess."** If grounding can't *prove* a single interpretation,
it refuses — every outcome other than `Committed` returns `Committed = null`.

## Weighted alias evidence is a separate signal

The same semantic contract also demonstrates optional alias weights in
`Tests/Grounding/SupplyChainAliasWeightTests.cs`. Entity, field, and
relationship aliases can carry a 1–100 application-declared weight. The
`AliasWeightEvidenceGate` checks the weighted evidence against a configured
minimum and reports inconclusive evidence when a weighted entity is below the
threshold. It does **not** alter the alias identity, replace retrieval scores,
or grant authorization. This keeps lexical evidence and authority as separate
boundaries.

## Case 1: genuine ambiguity, not just multiple candidates

`SupplyChainGroundingAmbiguityTests.cs` uses `"show me our active suppliers"`
against this sample's real schema specifically because "active" is
**structurally, legitimately** ambiguous here — a supplier can be:

- one with an open purchase order right now
  (`PurchaseOrder.Status == Open`, reached via `Supplier.purchaseOrders`), or
- one whose certification hasn't lapsed
  (`SupplierCertification.ValidTo`, reached via `Supplier.certifications`).

These aren't a strong match and a weak match — they're two independent,
equally valid business meanings that happen to share an English word. A
resolver that just took the top-scored candidate would silently commit to
one of them and execute a query the caller never precisely asked for. The
test's point is narrower than "does retrieval work" (it uses a fixed
`FakeLexicalSource`, not a live retrieval provider) — it's specifically:
*given two structurally-valid, materially-different candidates with tied
confidence, does the resolver refuse rather than pick one?*

## Case 2: unresolved — no candidate exists at all

`SupplyChainGroundingUnresolvedTests.cs` is a different failure shape:
`"customers with big accounts"`-style phrasing where a business-threshold
word (here, something like "risky" against `Supplier.RiskScore`) has **no
declared vocabulary** anywhere in the contract — no `Supplier.IsHighRisk`
flag, no named `RiskTier` value. The critical thing the test pins down:
grounding must **not invent a threshold** (`RiskScore > what, exactly?`) just
to make the query runnable. It must report `Unresolved` and name the exact
token that had no candidate — silently dropping that part of the phrase and
running the rest would answer a question the caller didn't ask, with no
indication anything was dropped.

## Case 3: budgets — failing closed on *cost*, not just on *meaning*

`SupplyChainGroundingBudgetTests.cs` is the third, orthogonal failure mode:
even when candidates plausibly exist, resolution has to stay inside
complexity bounds — `maxTokens`, `maxPathsExplored`, `timeout`,
`retrievalTimeout`, and cancellation are all exercised here against the real
generated contract. Every one of them fails the same way:
`GroundingOutcome.BudgetExceeded`, `Committed = null` — **never** a
best-effort answer built from a search that was cut off partway through. The
reasoning is the same shape as case 2: a search that ran out of budget
before proving uniqueness hasn't actually proven anything, so treating its
partial result as an answer would be indistinguishable from guessing.

## Why this belongs in the "Advanced" sample specifically

The starter sample's authorization model assumes the caller names an exact
capability (`get_my_orders`, `place_order`) — there's no natural-language
interpretation step at all, so there's nothing to be ambiguous about. This
sample's grounding tests exist because once you let an agent phrase things
in its own words against a rich schema (13 entities, dozens of relationships
— see `Semantic/Domain/Domain.cs`), *some* of those phrasings will
genuinely be ambiguous or under-specified, and "fail closed and say why"
has to be a tested guarantee, not an assumption.

---
Previous: [`02-High-Assurance-Scenarios.md`](./02-High-Assurance-Scenarios.md) · Next: [`04-Retrieval-Strategies.md`](./04-Retrieval-Strategies.md)
