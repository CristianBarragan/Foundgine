# Retrieval Strategies (Fuzzy / Full-Text / Search / Graph / Vector)

Files: `Tests/Retrieval/*.cs`. Provider under test:
`Foundgine.Providers.Storage.Sql.Retrieval.PostgresRetrievalCandidateSource`
(see also `src/Foundgine.Providers.Storage.Sql/README.md`).

## The concept: retrieval feeds grounding, grounding feeds planning

Where `03-Ambiguity-And-Grounding.md` covers *what happens* once candidate
interpretations exist, this file covers *where those candidates come from*
for a real, non-toy schema: `PostgresRetrievalCandidateSource` implements
several distinct `RetrievalStrategy` values, each backed by a different
PostgreSQL capability, each tested against fixtures shaped like this
sample's actual `Supplier`/`Product` tables.

## The five strategies, what each needs, and how to opt in

| Strategy | Backed by | Opt-in required | Test file |
|---|---|---|---|
| `Fuzzy` | `pg_trgm` (trigram similarity) | `FOUNDGINE_POSTGRES_CONNECTION` only — enabled by default on any Postgres with the extension | `SupplyChainFuzzyAndFullTextRetrievalTests.cs` |
| `FullText` | Native Postgres full-text search | `FOUNDGINE_POSTGRES_CONNECTION` only | `SupplyChainFuzzyAndFullTextRetrievalTests.cs` |
| `Search` | pg_search (ParadeDB BM25) | `FOUNDGINE_POSTGRES_CONNECTION` **and** `FOUNDGINE_POSTGRES_PGSEARCH=1` | `SupplyChainSearchRetrievalTests.cs` |
| `GraphSimilarity` | Apache AGE | `FOUNDGINE_POSTGRES_CONNECTION` **and** `FOUNDGINE_POSTGRES_AGE=1` | `SupplyChainGraphSimilarityRetrievalTests.cs` |
| `Vector` | *(reserved for a future pgvector provider)* | n/a — always short-circuits | `SupplyChainRetrievalCapabilityTests.cs` |
| `Relational` | *(documented no-op)* | n/a | `SupplyChainRetrievalCapabilityTests.cs` |

**Why two strategies need a second, explicit opt-in on top of the connection
string:** pg_search and Apache AGE are not installed on a vanilla PostgreSQL
image — unlike `pg_trgm` and full-text search, which ship with core
Postgres. Gating them behind their own environment variables means CI and a
typical local Postgres stay green (those tests are *skipped*, not *failed*)
while anyone who has actually installed the extension gets full coverage.
This is the same "opt-in, not opt-out" philosophy the claims validator uses
for unrecognized claim keys (see `01-Claims-And-Authorization.md`) — absence
of a capability degrades gracefully instead of failing loud in an
environment that never claimed to support it.

## Fuzzy vs. full-text: two different questions, not two strengths of the same one

It's tempting to think of `Fuzzy` and `FullText` as "loose" vs. "strict"
matching. They're actually answering different questions:
- **Fuzzy** (`pg_trgm`): "what's *character-level similar* to this string?"
  — this is what catches a misspelled supplier name
  (`Fuzzy_retrieval_matches_a_misspelled_supplier_name`), because trigram
  similarity doesn't care about word boundaries or meaning.
- **FullText**: "what documents contain these *words* (after stemming/
  stopword removal)?" — this is a token/relevance match, not a
  character-distance match, and won't catch a misspelling the way Fuzzy does.

## `Search` (BM25) — why it's a separate strategy from `FullText`

Native Postgres full-text search and ParadeDB's pg_search both operate over
text, but BM25 ranking (term frequency, inverse document frequency, field-
length normalization) produces materially different relevance ordering for
free-text queries like `"metal fabrication"` against a `Supplier.Name`
field — `SupplyChainSearchRetrievalTests.cs`'s test name
(`Search_retrieval_ranks_suppliers_by_bm25_relevance`) is explicit that
ranking quality, not just match/no-match, is what this strategy is tested
for.

## `GraphSimilarity` — a genuinely separate index, not a mirror of foreign keys

The doc comment on `SupplyChainGraphSimilarityRetrievalTests.cs` makes a
subtle point worth restating: the AGE graph models "two suppliers are
neighbor-similar when they both connect to the same purchase-order vertex"
— **even though**, relationally, a single purchase order has exactly one
owning supplier (a real foreign key, one-to-many). The AGE graph is a
separate, purpose-built retrieval index shaped for a *co-sourcing/risk
similarity* question ("which suppliers are structurally similar to this
one?"), not a graph-shaped copy of the relational schema. This is worth
internalizing if you're adding a new graph retrieval case: the question is
"what similarity structure does this business question need," not "how do I
represent my foreign keys as edges."

## `Vector` and `Relational` — the two strategies that never touch Postgres

`SupplyChainRetrievalCapabilityTests.cs` is the one file in this set that
needs **no real database at all** — it builds a syntactically valid but
unreachable connection string (`Host=127.0.0.1;Port=1;...;Timeout=1`) and
asserts that every strategy either:
- short-circuits before issuing any command (an opt-in gate that's off, a
  request-shape validation failure, or `Vector` being reserved), or
- is a documented no-op (`Relational`).

This is provider-*wiring* coverage, deliberately separate from the
provider-*behavior* coverage in the other four files: it proves the gating
logic itself is correct (a disabled strategy never reaches PostgreSQL to
find out it's disabled) without needing any infrastructure to do so.

## `find_top_supplier_overdue_orders`'s own fallback: a lighter cousin of `PostgresRetrievalCandidateSource`

Everything above exercises `PostgresRetrievalCandidateSource` directly
against `SupplyChainSemanticModel.Metadata` and a throwaway fixture schema —
it's provider-conformance testing for `IApproximateCandidateSource`, not a
capability. `Semantic/Api/Mcp/Program.cs`'s `SupplyChainExecutionService`
implements the same three practical strategies (`Fuzzy`, `FullText`,
`Search`) a second time, independently, scoped to one real question: *does
the `supplierName` a caller passed to `find_top_supplier_overdue_orders`
approximately match a supplier in the requested state?*

It's deliberately **not** routed through `PostgresRetrievalCandidateSource`.
That type resolves against `_metadata.GetEntity`/`ResolveField` and the
generated `SupplyChainSemanticModel.Metadata` catalog — the right shape when
retrieval has to be generic across arbitrary entities/fields chosen at
grounding time. `find_top_supplier_overdue_orders` already knows, at compile
time, that it's matching `Supplier.Name` scoped to one `state` — so
`SupplyChainExecutionService.TryApproximateSupplierMatchAsync` is three
small, direct SQL queries (`TryFuzzyAsync`, `TryFullTextAsync`,
`TrySearchAsync`) against the real `suppliers` table, run in that order,
stopping at the first strategy that returns anything:

| Order | Strategy | SQL shape | Gate |
|---|---|---|---|
| 1 | `Fuzzy` | `similarity(supplier_name, @name)` / `supplier_name % @name` | always on — `Database/Program.cs` provisions `CREATE EXTENSION IF NOT EXISTS pg_trgm` and a `gin_trgm_ops` index on `suppliers.supplier_name` as part of the sample's own schema |
| 2 | `FullText` | `ts_rank_cd(to_tsvector(...), websearch_to_tsquery(...))` | always on — native Postgres, no extension needed |
| 3 | `Search` | `pdb.score(supplier_id)` / `supplier_name \|\|\| @name` | `FOUNDGINE_POSTGRES_PGSEARCH=1`, same gate as `SupplyChainSearchRetrievalTests.cs` above |

Two things carried over deliberately from `PostgresRetrievalCandidateSource`
and from the tie-break case earlier in this walkthrough:

- **Ordering by looseness.** Fuzzy (character-level) is tried before
  FullText (token-level) before Search (BM25-ranked), exactly the same
  reasoning as "Fuzzy vs. full-text" above — try the strategy least likely
  to produce a false positive on a short proper noun like a supplier name
  first.
- **A match is evidence, not authority.** Finding an approximate candidate
  never auto-resolves the capability. It returns `status:
  "clarification_needed"` with `evidence.strategy` naming which one
  matched, exactly the same "ask, don't guess" contract the exact-tie case
  uses — see `03-Ambiguity-And-Grounding.md` and the README's outcomes
  table. The caller still has to name the supplier back before anything is
  authorized or executed. If all three strategies come back empty, the
  capability falls through to `not_found` with `strategiesTried` listing
  what was actually attempted, so a caller (or this doc) never has to guess
  whether fuzzy matching ran and simply found nothing versus never running
  at all.
- **Fails soft, not loud, when an extension is missing.** Each of the three
  helper methods catches `PostgresException` and returns no candidates
  rather than throwing — the same "opt-in, not opt-out" posture as the gate
  table above, applied at the query level instead of the test-skip level.

---
Previous: [`03-Ambiguity-And-Grounding.md`](./03-Ambiguity-And-Grounding.md) · Next: [`05-Adversarial-Security-Testing.md`](./05-Adversarial-Security-Testing.md)
