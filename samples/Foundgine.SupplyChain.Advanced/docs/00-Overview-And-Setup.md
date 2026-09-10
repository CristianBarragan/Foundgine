# Foundgine Supply Chain — Advanced Sample: Concept Docs

This `docs/` folder is a companion to the sample's own top-level
`README.md` and `Fundgine - SupplyChain-E2E-Step-by-Step-Guide.md`. Those
two tell you **how to run it**; these five tell you **why it's built this
way**, one concern at a time, cross-referenced to the exact test files that
prove each claim.

Read them in order — each one assumes the previous one's vocabulary:

1. [`01-Claims-And-Authorization.md`](./01-Claims-And-Authorization.md) —
   identity vs. caller-asserted claims, the claim-validation pipeline, and
   the five-rule authorization policy (entity / field / relationship /
   predicate / named-operation).
2. [`02-High-Assurance-Scenarios.md`](./02-High-Assurance-Scenarios.md) —
   two read-side business scenarios (recursive BOM supplier risk,
   fulfillment planning) that stay correct and bounded against cycles,
   unbounded depth, and cross-tenant leakage; plus the executable
   adversarial-invariant check.
3. [`03-Ambiguity-And-Grounding.md`](./03-Ambiguity-And-Grounding.md) — why
   an ambiguous or unrecognized phrase must make grounding refuse
   (`Unresolved` / `BudgetExceeded`) rather than guess.
4. [`04-Retrieval-Strategies.md`](./04-Retrieval-Strategies.md) — where the
   candidate interpretations that feed grounding come from: five retrieval
   strategies (`Fuzzy`, `FullText`, `Search`, `GraphSimilarity`, `Vector`),
   what each needs, and how each degrades gracefully when its backing
   capability is absent.
5. [`05-Adversarial-Security-Testing.md`](./05-Adversarial-Security-Testing.md) —
   the mechanisms underneath all of the above: bounded graph traversal,
   graph-level (subtree) authorization pruning, fail-closed open-intent
   mutation authoring, and the field/join-key leak boundary at execution
   time.

## What's different from the `Foundgine.SupplyChain` starter sample

The starter sample (`samples/Foundgine.SupplyChain`) is a closed API: a
fixed set of MCP tools (`get_my_orders`, `place_order`, …) with fixed
arguments, and one flat actor→token→customer authorization check. This
sample opens that surface up — an agent can describe an arbitrary read or
mutation shape at runtime (`ReadIntent`, `SemanticMutationIntentBuilder`),
and can phrase requests in natural language that has to be *grounded*
against the schema before it can be planned at all. Every doc in this
folder exists because that openness introduces a question the starter
sample never has to answer, and each doc's test files are the proof that
the answer holds under adversarial input, not just the happy path.

## Prerequisites

Everything below assumes the same base setup as the starter sample, plus a
few extras this sample exercises optionally:

- **.NET 9 SDK** — `dotnet --version` should print a 9.x version.
- **Docker Desktop** — for PostgreSQL (required) and, optionally, for
  running with the `pg_search` (ParadeDB) and Apache AGE extensions enabled
  (see doc `04`).
- A clone of this repository — the sample uses project references into
  `src/csharp/Foundgine.Core`, `src/csharp/Foundgine.Runtime` (whose `ControlPlane/` folder
  now holds the authority/recovery concerns formerly in the standalone
  `Foundgine.Security.Authority` package), and `src/csharp/Foundgine.Providers`,
  exactly like the starter sample does.
- **PowerShell** (`pwsh`) — the sample's runner scripts
  (`run-supply-chain.ps1`, `publish-supply-chain-report.ps1`,
  `merge-supply-chain-pentest-report.ps1`) are PowerShell, cross-platform
  via `pwsh` on macOS/Linux.

### Environment variables this sample reads

| Variable | Required for | Notes |
|---|---|---|
| `FOUNDGINE_POSTGRES_CONNECTION` | Any retrieval test in doc `04` | Standard Npgsql connection string. Omit it and those tests skip rather than fail. |
| `FOUNDGINE_POSTGRES_PGSEARCH=1` | `Search` (BM25) retrieval tests | Requires the ParadeDB `pg_search` extension installed on the target Postgres. |
| `FOUNDGINE_POSTGRES_AGE=1` | `GraphSimilarity` retrieval tests | Requires the Apache AGE extension installed on the target Postgres. |
| `SUPPLY_CHAIN_CUSTOMERS`, `SUPPLY_CHAIN_STEPS`, `SUPPLY_CHAIN_SEED` | The stochastic agent-workload runner (`run-supply-chain.ps1`) | See the sample's top-level `README.md` for the full run command. |

None of the five concept docs above require the optional Postgres
extensions to be readable — the tests they describe are written to skip
cleanly (not fail) when an extension isn't present, which is itself part
of what doc `04` explains.

### Running just the test suite

If you only want to see the behaviors these docs describe, without running
the full agent-workload benchmark:

```bash
cd samples/Foundgine.SupplyChain.Advanced/Semantic
dotnet test Tests/Foundgine.SupplyChain.Advanced.Tests.csproj
```

This runs every test referenced across docs `01`–`05`. Tests gated behind
the Postgres environment variables above will report as skipped, not
failed, if those variables aren't set.

For the full end-to-end run (PostgreSQL + MCP service + stochastic agent
workload + merged pentest report), see the top-level `README.md` in this
sample's root directory and `Fundgine - SupplyChain-E2E-Step-by-Step-Guide.md`.
