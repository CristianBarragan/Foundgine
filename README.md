<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs-site/assets/logo/foundgine-logo-dark.png">
  <img src="docs-site/assets/logo/foundgine-logo.png" alt="Foundgine" width="360">
</picture>

[![NuGet Version](https://img.shields.io/nuget/v/Foundgine.Core?label=NuGet%20Version)](https://www.nuget.org/packages/Foundgine.Core/)
[![NuGet Downloads](https://img.shields.io/endpoint?url=https%3A%2F%2Fcristianbarragan.github.io%2FFoundgine%2Fdocs-site%2Fassets%2Ffoundgine-nuget-downloads.json)](https://www.nuget.org/packages?q=Foundgine)
[![Unit Tests](https://img.shields.io/github/actions/workflow/status/CristianBarragan/Foundgine/build.yml?branch=main&job=unit-tests&label=Unit%20Tests)](https://github.com/CristianBarragan/Foundgine/actions/workflows/build.yml)
[![Integration Tests](https://img.shields.io/github/actions/workflow/status/CristianBarragan/Foundgine/build.yml?branch=main&job=integration-tests&label=Integration%20Tests)](https://github.com/CristianBarragan/Foundgine/actions/workflows/build.yml)
[![Performance (Hot Chocolate)](https://img.shields.io/github/actions/workflow/status/CristianBarragan/Foundgine/build.yml?branch=main&job=benchmark-build-hotchocolate&label=Performance%20%28Hot%20Chocolate%29)](https://github.com/CristianBarragan/Foundgine/actions/workflows/build.yml)
[![Performance (Foundgine)](https://img.shields.io/github/actions/workflow/status/CristianBarragan/Foundgine/build.yml?branch=main&job=benchmark-build-foundgine&label=Performance%20%28Foundgine%29)](https://github.com/CristianBarragan/Foundgine/actions/workflows/build.yml)
[![Security Audit](https://img.shields.io/github/actions/workflow/status/CristianBarragan/Foundgine/build.yml?branch=main&job=security-penetration&label=Security%20Audit)](https://github.com/CristianBarragan/Foundgine/actions/workflows/build.yml)

# Foundgine

Foundgine gives AI agents and other callers a way to express what they want, while your application retains full control over how anything is executed. It introduces a single, application‑controlled semantic execution boundary that sits between:

 * caller intent,

 * your domain meaning,

 * your authorization rules,

 * and the actual operations performed on your data, APIs, GraphQL, MCP tools, or backend systems.

This boundary ensures that agents can propose actions, but only your application decides **what is allowed, what is safe, and what is actually executed.**

[**Go to website →**](https://cristianbarragan.github.io/Foundgine/docs-site/)

# The problem

As applications expose more functionality to callers, you can end up with lots of individual tools/endpoints, each containing its own validation, authorization, query logic, and business rules.

Foundgine centralizes that responsibility into a semantic execution boundary. The caller expresses intent, while the application remains responsible for deciding what that intent means, what is allowed, and how it gets executed.

A complex application may have several ways to express an operation:

```text
Application code
GraphQL
JSON
AI-generated intent
```

Without a common semantic execution layer, each surface tends to grow its own rules for:

- what entities and fields exist;
- which relationships can be traversed;
- which filters are valid;
- what the caller is authorized to access; and
- how the request becomes database or service operations.

That produces duplicated semantics and inconsistent security boundaries. 

## The idea

Retrieval can discover candidates and evidence, but **retrieval is not authorization**. The application owns identity and policy; providers execute the already-authorized artifact. **Foundgine** centralizes this responsibility.

## Get started

The fastest path is the Supply Chain sample pair:

- **Starter:** [`src/csharp/samples/Foundgine.SupplyChain`](src/csharp/samples/Foundgine.SupplyChain) — the smallest realistic application boundary.
  - [Build it step by step](src/csharp/samples/Foundgine.SupplyChain/SupplyChain-Starter-Tutorial.md)
  - [Understand why it is structured this way](src/csharp/samples/Foundgine.SupplyChain/Foundgine-SupplyChain-Explained.md)
- **Advanced:** [`src/csharp/samples/Foundgine.SupplyChain.Advanced`](src/csharp/samples/Foundgine.SupplyChain.Advanced) — richer semantics, grounding, retrieval, authorization and adversarial testing.
  - Start at [`docs/00-Overview-And-Setup.md`](src/csharp/samples/Foundgine.SupplyChain.Advanced/docs/00-Overview-And-Setup.md) and follow 01–05.

For the conceptual path, use [`docs/README.md`](docs/README.md) or the [documentation site](https://cristianbarragan.github.io/Foundgine/docs-site/).

# A concrete example

Two callers can ask for the same thing in different words:

- **Canonical:** “show me overdue purchase orders from our top supplier in Texas”
- **Paraphrase:** “show me the overdue buys from our top seller in Texas”

Foundgine does not treat the paraphrase as a fuzzy guess at a *different* operation. In the Supply Chain semantic contract, `Buy`/`Buys` are declared aliases of `PurchaseOrder`, and `Seller` is a declared alias of `Supplier`. Both sentences are grounded onto the **same canonical semantic identities** before authorization or planning ever runs — the diagram below follows one request all the way from words to a database call.

*Tests:* [`SupplyChainGroundingAliasTests.cs`](src/csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Tests/Grounding/SupplyChainGroundingAliasTests.cs) (advanced Supply Chain sample) · [`SemanticAliasSynonymGroundingTests.cs`](tests/Foundgine.Semantics.Tests/SemanticAliasSynonymGroundingTests.cs) (core semantics).

<p align="center"><img src="docs/assets/overdue-purchase-orders-alias-flow.svg" alt="Foundgine alias-matched Supply Chain request from caller intent through semantic resolution, authorization, planning, PostgreSQL execution and evidence." width="100%"></p>

### The request, layer by layer

| # | Layer | What happens |
|---|---|---|
| 1 | **Caller intent** | The caller sends either sentence. Neither one contains SQL or provider instructions. |
| 2 | **Intent representation** | The request becomes structured intent — the caller never constructs a physical query directly. |
| 3 | **Semantic Model** | The generated contract exposes canonical meanings and their declared aliases: `PurchaseOrder ← Buy, Buys` and `Supplier ← Vendor, Seller`. |
| 4 | **Semantic Operation Graph** | The request becomes application meaning: overdue purchase-order semantics, a ranked “top supplier” relationship, and a `Texas` constraint. |
| 5 | **Retrieval** | Relational, fuzzy/full-text, BM25/search, or graph strategies propose candidates and evidence. **They never grant authority.** |
| 6 | **Semantic Resolution** | `buys → PurchaseOrder` and `seller → Supplier`. The aliases normalize to the same canonical identities as the original wording. |
| 7 | **Authorization** | Application policy runs against the resolved semantic graph and caller identity. Retrieval results cannot bypass this step. |
| 8 | **Plan Binding** | The authorized decision is bound to a provider-independent execution plan. |
| 9 | **ExecutionIR** | The executable artifact carries the resolved plan and its authorization provenance across the execution boundary. |
| 10 | **Provider** | Only now does a physical provider — PostgreSQL, here — receive the already-authorized artifact. |
| 11 | **Execution** | The provider executes the constrained plan; it does not reinterpret caller vocabulary. |
| 12 | **Evidence** | The result carries evidence of what was resolved and executed. Evidence records what happened — **it does not grant authority.** |

> **The invariant:** alias matching changes vocabulary, not authority. “Buys” does not create a new capability, and “seller” does not create a second supplier meaning — both are application-declared paths to identities that already exist.

# When more than one meaning is legal

Aliases collapse *different words* onto *one* meaning. Sometimes the ambiguity runs the other way: the **same word** is a legal match for **two different meanings at once**, and neither the graph nor a retrieval score can tell them apart on its own.

Take the request **“active customers”**. Both of the following are structurally valid readings:

- a customer whose **account is enabled** (`Customer.AccountEnabled`)
- a customer who **placed a recent order** (`Customer.HasRecentOrder`)

A fuzzy/BM25/vector retriever can legitimately return both, with close scores (`0.91` vs. `0.89`). Foundgine does not break the tie by picking whichever scored higher: a higher retrieval score is not evidence of what the caller meant, and authorization can’t rescue a wrong guess — a request built from the wrong meaning is still a *fully authorized* request. It would just be a perfectly authorized misunderstanding.

<p align="center"><img src="docs/assets/lexical-grounding-clarification-flow.svg" alt="Foundgine lexical grounding when two meanings are legal: fuzzy retrieval returns tied candidates, both form a valid semantic path, grounding reports RequiresClarification, and the caller picks one before authorization runs." width="100%"></p>

### What Foundgine does instead of guessing

| Stage | What happens |
|---|---|
| **Retrieval (fuzzy)** | Every plausible reading comes back as a candidate, each with its own score and evidence. Retrieval only ever proposes — it never decides. |
| **Graph-constrained resolution** | Each candidate is checked against the frozen semantic contract. Both `AccountEnabled` and `HasRecentOrder` form a legal path. A legal path only proves an interpretation is *possible* — not that it is the one *intended*. |
| **Grounding decision** | `SemanticLexicalResolver.Ground` compares the two paths’ **signatures** — what each one means, not how it got there. The signatures differ and neither dominates on confidence, so the outcome is `GroundingOutcome.RequiresClarification`: `Committed` stays `null`, and both readings are listed in `CompetingInterpretations`, each with its own steps, confidence, and evidence. |
| **Caller chooses** | The competing meanings are surfaced back as a clarifying question — *“Did you mean customers with an enabled account, or customers with a recent order?”* — instead of silently executing a guess. |
| **Same boundary as everyone else** | Once the caller picks one, that single interpretation goes through the exact same Authorization → Planning → Execution path as any other request. |

> **The invariant:** a legal semantic path is not proof of intent. When retrieval genuinely can’t tell two meanings apart, Foundgine surfaces the ambiguity instead of silently authorizing a coin flip.

The same mechanism fails closed the same way in two other cases: when a resource limit (token count, search budget, timeout) stops the search before it can prove there is only one meaning (`GroundingOutcome.BudgetExceeded`), and when no legal interpretation exists at all (`GroundingOutcome.Unresolved`). Neither one ever falls back to a best-effort guess.

Read more: **[Lexical grounding](docs/LEXICAL-GROUNDING.md)** covers fuzzy retrieval, the resolver’s complexity bounds, and worked adversarial examples end-to-end. **[Grounding decisions](docs/GROUNDING-DECISIONS.md)** covers the full `GroundingDecision` shape, the difference between “different evidence for the same meaning” and “different meanings,” and the complete `active customers` walkthrough, backed by a passing test.

## Why the boundary matters

The number of independent execution surfaces is a security and maintenance multiplier. A tool-per-capability design can give an agent dozens of places where authorization, tenant filtering and query construction are implemented differently. Foundgine centralizes the semantic decision without making a transport or database the center of the architecture.

For the deeper rationale, read:

- [`docs/WHY-FOUNDGINE.md`](docs/WHY-FOUNDGINE.md)
- [`docs/APPLICATION-CATEGORIES.md`](docs/APPLICATION-CATEGORIES.md)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/AUTHORIZATION.md`](docs/AUTHORIZATION.md)
- [`docs/SECURITY.md`](docs/SECURITY.md)
- [`docs/AI-AGENT.md`](docs/AI-AGENT.md)

## Package shape

The current source layout is consolidated into four publishable packages:

| Package | Responsibility |
|---|---|
| `Foundgine.Core` | Semantic model, metadata, intent, planning and provider-independent contracts |
| `Foundgine.Runtime` | Application-facing orchestration, authorization and execution |
| `Foundgine.Providers` | Storage, AI/model, MCP, AOT and other concrete integrations |
| `Foundgine.Extensions` | Optional framework integrations such as Hot Chocolate GraphQL |

The normal application starting point is `Foundgine.Runtime` + `Foundgine.Providers`. See the [package guide](docs-site/packages/) for the current boundary map.

## Evidence

The repository contains controlled benchmarks and deterministic security tests. The public evidence distinguishes **measured** tool calls, latency, RPS and success/failure counts from **estimated** context metrics.

- [Agent benchmark explorer](https://cristianbarragan.github.io/Foundgine/docs-site/agent-benchmark/)
- [Supply Chain E2E](https://cristianbarragan.github.io/Foundgine/docs-site/agent-benchmark/supply-chain/)
- [Security PenTest](https://cristianbarragan.github.io/Foundgine/docs-site/samples/pentest/)
- [`src/csharp/benchmarks/AgentEndToEnd/README.md`](src/csharp/benchmarks/AgentEndToEnd/README.md)

Benchmark results are workload-specific and should not be generalized beyond the published experiment.

### Latest red-team pentest result

A live adversarial run of `Foundgine.RedTeam` against the Advanced Supply Chain sample sent 18 attack attempts — cross-tenant probes, role/identity claim spoofing, claim-scope widening, write/capability escalation, and unauthenticated-actor calls — across both the semantic authorization API and the execution/tool-calling API. Every adversarial attempt was denied or blocked; only the two intentionally-legitimate baseline calls succeeded. Full attack-by-attack results, why the two APIs' error shapes intentionally differ, and the server-side classification/correlation-id logging now implemented for the execution API: [`docs/SECURITY.md#red-team-pentest-results-advanced-supply-chain-sample`](docs/SECURITY.md#red-team-pentest-results-advanced-supply-chain-sample).

## Development

```bash
dotnet restore
dotnet build
dotnet test
```

PostgreSQL integration testing: [`docs/POSTGRES-E2E.md`](docs/POSTGRES-E2E.md).

Foundgine's architecture is specified independently of this implementation as
the **Semantic Execution (SES) standard**: [`standards/semantic-execution/`](standards/semantic-execution/README.md).
The standard is the authority — see [`SES-104`](standards/semantic-execution/SES-104-foundgine-test-mapping.md)
for the current implementation gap matrix and [`conformance/known-gaps.json`](standards/semantic-execution/conformance/known-gaps.json)
for the machine-readable status of every tracked item.

## Release 2.0.3

**Current release: 2.0.3 · .NET 9**

The 2.0.3 release adds candidate truncation diagnostics to semantic lexical grounding: when a token's candidate set is cut down to `candidateLimit` before graph search ever runs, the resolver now records the cut and, if the highest-scoring discarded candidate was within the ambiguity margin of what was kept, requires clarification instead of silently committing to an interpretation that was never actually checked against a competing meaning. It builds on the 2.0.2 release, which fixed the compact-name lexical fallback (e.g. `purchase order` → `purchaseorder`) so it is reliably reached instead of being starved of retrieval-timeout budget by exhaustive per-token lookups.

See [`CHANGELOG.md`](CHANGELOG.md) for the release notes.

Foundgine is licensed under the Apache License 2.0.
