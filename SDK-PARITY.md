# Foundgine SDK and runtime parity contract

Status: initial repository audit, 2026-11-14. This document distinguishes equivalent capabilities from
language-specific conveniences and from known unimplemented platform ports. A matching method name is not
evidence of behavioural parity; shared conformance tests and runtime evidence are required.

## 1. SDK tiers

Foundgine currently ships two different SDK tiers. They must not be conflated:

| Tier | Implementations | Contract |
|---|---|---|
| Platform SDK/runtime | C#, Java | Semantic model/configuration, metadata, planning and execution, authorization/security, mutation execution, provider integrations, and framework extensions. |
| Transport client SDK | TypeScript, Python, Rust/WASM | MCP client/session, tool discovery and invocation, SSE notifications, and Temporal workflow HTTP client/watch helpers. No server hosting or business logic. |

Python/TypeScript/Rust are not expected to implement the C#/Java semantic engine or persistence providers. They must implement the same *client wire capabilities*.
C# and Java must implement the same platform contract, with provider capabilities enabled only after provider-specific translation and runtime conformance tests pass.

## 2. Transport client parity

| Capability | TypeScript | Python async | Rust/WASM JS facade | Status |
|---|---|---|---|---|
| MCP initialize/session headers/teardown | Yes | Yes | Yes | Aligned in scope |
| JSON-RPC request/notification | Yes | Yes | Yes | Aligned in scope |
| Paged `tools/list` | Yes | Yes | Yes | Aligned; Rust now exposes canonical aliases |
| `tools/call`, tool errors as data | Yes | Yes | Yes | Aligned; Rust now exposes canonical aliases |
| Resumable SSE notifications | Yes | Yes | Yes | Rust option parity still needs work |
| Cancellation and best-effort MCP cancellation notice | AbortSignal | `asyncio.Event` and task cancellation | AbortSignal | Equivalent intent; host idioms differ |
| Temporal process catalog/describe/start/status/decision/cancel | Yes | Yes | Yes | Aligned in core routes |
| Workflow wait/watch/start-and-watch | Yes | Yes | Yes | Async watch supported; Python sync facade is incomplete |
| Typed error taxonomy | Classes | Exception classes | Rust enum + JS error classes | Equivalent categories; native representation differs |
| Injected transport | `fetch` | `httpx.AsyncClient` | `fetch` | Intentional language/runtime difference |
| Sync facade | No | Yes | No | Python convenience, not a parity requirement |
| Configurable HTTP timeouts | No SDK option | Yes | Host-controlled | Python convenience; document as extra |

### Required follow-up gaps

1. **Python sync workflow facade:** `SyncWorkflowClient` exposes process/describe/start/status/decide/cancel/wait, but not `watch` or `start_and_watch`. Either add synchronous watch/iterator support or explicitly document the sync facade as a subset; do not claim full API mirroring until decided.
2. **Watch state representation:** TypeScript exposes `WorkflowStatus` without a required `isFinished` field; Python provides `is_finished(status)`; Rust's JS facade adds `isFinished`. Keep wire payloads identical and document derived convenience fields consistently.
3. **Reconnect semantics:** The WASM wrapper currently documents reconnect after clean stream completion only; TypeScript resumes after a clean end and after an established stream drops. Align the retry policy or explicitly standardize and test the narrower policy across all clients.
4. **Shared conformance:** Expand the shared vectors beyond SSE frame parsing to cover MCP session lifecycle, notification replay/resume, workflow routes, decision bodies, terminal statuses, timeout, cancellation and error mapping. Every SDK must run the same vectors where applicable.

### Normalization changes in this increment

- TypeScript workflow path segments now use RFC 3986 unreserved characters only, matching Python and Rust (`!'()*` are percent-encoded).
- TypeScript and Rust URL joining now normalize repeated boundary slashes, matching Python's `rstrip`/`lstrip` behaviour.
- Rust/WASM `FoundgineClient` now exposes `listCapabilities`, `execute`, and top-level `notifications` to match TypeScript/Python naming. Existing `listTools` and `callTool` remain as backwards-compatible aliases.
- Rust/WASM notifications now accept `lastEventId` and `reconnectDelayMs`, matching TypeScript; Python uses snake_case equivalents.

## 3. Platform parity: C# and Java

The intended platform parity surface includes:

- Semantic model and configuration; entity/field/relationship metadata and maps.
- Semantic query/intent construction, canonicalization and validation.
- Planning, execution IR, provider contracts, result/evidence/receipt boundaries.
- Authorization policy, predicates, tenant isolation, warrants, delegation and replay protection.
- Mutation planning/execution, dependencies, batching, transactions and idempotency.
- MCP server/provider integration and Temporal workflow integration.
- Persistence providers, vector/retrieval integrations, GraphQL and identity integrations where the C# package offers them.

The Java tree has broad core/security/extension parity tests, but the repository itself records concrete gaps:

- The Java advanced supply-chain sample **does contain** `TopSupplierOverdueOrdersService` and registers the ambiguity-resolution MCP capability. It implements exact-name resolution, `pg_trgm` fuzzy matching, PostgreSQL full-text, and optional `pg_search`/BM25. However, it executes this capability through a dedicated JDBC service and uses a stable hash of a fixed read shape rather than the C# sample's full semantic planner/authorizer/ExecutionIR fingerprint. The Java README's “Where to go next” and tutorial still claim the ambiguity feature is not ported, contradicting the implementation and the README's own package table. Reconcile the docs and test the actual implementation before claiming full parity.
- The C# advanced sample additionally documents GraphSimilarity retrieval (optional Apache AGE) and tests for its provider mechanism. The Java sample explicitly says GraphSimilarity does not apply to this fixed supplier-name capability; that is a sample-level difference, not proof that the Java provider lacks graph retrieval support.
- Java `foundgine-extensions` contains framework-neutral GraphQL adapters, Temporal integration and Keycloak integration. C# `Foundgine.Extensions` specifically integrates Hot Chocolate. These are comparable integration capabilities, but not identical framework adapters or dependency surfaces.
- Java JPA parity is explicitly `implemented-unverified` for several capabilities. The machine-readable `src/java/conformance/orm-jpa-parity-plan.json` requires real H2/PostgreSQL execution evidence and native-image assessment before declaring those capabilities supported. Source presence or test definitions alone are not certification.
- The C# sample includes additional advanced retrieval/grounding and GraphQL penetration-test surfaces; these need capability-by-capability comparison, not a broad “Java parity” assertion.

The ORM/provider rule remains: advertise only capabilities with working provider-specific translation and passing database/runtime conformance. Unsupported shapes must fail closed; a capability declaration alone is not proof of implementation.

## 4. Release gates

A release may claim parity only when:

1. Public API inventories are generated/checked for each SDK tier.
2. Shared wire/conformance vectors pass in every applicable language.
3. Cross-runtime canonicalization and security vectors pass (including C# ↔ Java).
4. Provider capability declarations match tested translation and runtime behaviour.
5. Integration tests run against real HTTP/MCP/Temporal endpoints and real supported databases where applicable.
6. Package builds, type checks, lint, tests and pack/install checks pass on the declared runtime matrix.
7. Documentation lists intentional extras and remaining gaps without contradictory claims.

Do not mark the full repository as feature-parity complete until the required follow-up gaps above and the Java platform/sample gaps are closed or explicitly scoped out.
