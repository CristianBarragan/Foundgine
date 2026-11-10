# Foundgine dual-runtime hardening plan (.NET + Java)

This plan records code-verified findings from the Step 70 workspace and separates existing controls from work still required. The accompanying audit text contains useful hypotheses, but a hypothesis is not treated as a confirmed defect until the repository or a reproducible run supports it.

## Evidence-based findings

| Area | .NET evidence | Java evidence | Status / decision |
|---|---|---|---|
| PostgreSQL outbox contention | `Foundgine.Providers/Outbox/PostgresOutbox.cs` leases rows with `FOR UPDATE SKIP LOCKED`; `PostgresOutboxIntegrationTests.Concurrent_leases_never_overlap` covers competing leases. | `foundgine-providers/.../outbox/PostgresOutbox.java` uses the same leasing and retention strategy; `PostgresInfrastructureIntegrationTest.outbox_concurrentDispatchersLeaseDisjointMessages` covers concurrent dispatchers. | Already implemented on both sides. Add broader throughput/deadlock measurements before changing SQL. |
| Tenant/security context | `SecurityExecutionContext`, `ISecurityExecutionContextProvider`, provider extensions and warrant authorization carry trusted tenant context; engine passes tenant into authorization/evaluation. | Matching `SecurityExecutionContext`, provider interface/extensions, Keycloak adapter and warrant authorization exist; runtime engine passes tenant. | Core boundary exists on both sides. Remaining work: prove that every planner/optimizer and provider path receives the same trusted tenant scope before schema/relationship discovery. |
| MCP transport contention | `McpJsonRpcClient` in the supplied pen-test fixture sends independent HTTP POST requests with unique JSON-RPC IDs. | Java has `FoundgineMcpAgentClient` and MCP client parity tests. | The supplied claim about one shared locked stdin/stdout stream is not established by these clients. Benchmark transport topology separately from server execution. |
| Audit throughput/durability | `IAuditLog.Record` remains synchronous; default `InMemoryAuditLog` is process-local. `BufferedDurableAuditLog` adds a bounded `Channel` adapter and `IAuditEventStore` acknowledgement contract. | `IAuditLog.record` remains synchronous; default `InMemoryAuditLog` is process-local. `BufferedDurableAuditLog` adds a bounded `ArrayBlockingQueue` adapter and `AuditEventStore` acknowledgement contract. | Steps 72–73 provide lossless backpressure, per-event acknowledgement, failure propagation, drain-on-close and queue/persistence telemetry. Governors remain synchronous but fail closed: each audit write occurs before routing or returning an allow result, and sink exceptions abort the governance call. No production durable sink implementation is supplied yet. |
| Benchmark telemetry | Run 2/Run 4 artifacts contain `SampleCount: 0`; collector suppresses native command errors and silently skips unresolved containers/stats failures. | The same benchmark harness exercises protocol/API services; Java provider runtime metrics are not independently instrumented by these files. | Collector hardened in this step: errors are persisted, zero samples are explicit failure, and benchmark runners check the summary exit code. A new run is still needed to identify environment-specific Docker failures. |
| Semantic plan cache | No verified reusable plan-cache contract found in the inspected path. | No verified equivalent cache contract found in the inspected path. | Design required. Cache only immutable, parameterized structural compilation artifacts; rebind authorization, tenant, warrants, data filters and request values on every execution. Never cache an allow decision. |
| Short-lived session leases / plan pinning | Warrant and security context infrastructure exists, but no complete lease exchange + rotation contract was established by this inspection. | Equivalent warrant/context primitives exist; complete lease exchange was not established. | Design and implement as a shared conformance contract: issuer, audience, tenant, schema/operation scope, expiry, nonce, key id, revocation/rotation, replay handling and signature algorithm policy. |
| Human approval suspension | Inline `PlanApproval` abstractions and Temporal integration exist. | Plan approval abstractions and Temporal integration exist. | Durable suspension/resumption, approval binding to immutable plan digest, signer identity and expiry need explicit cross-runtime conformance tests. Never deserialize and execute a caller-modified plan after approval. |
| AOT type parity | Roslyn generator is available and can inspect nullable annotations and source symbols. | Java annotation processor sees source elements, but generic erasure and annotation-retention semantics differ. | Add shared schema-driven cases for nullability, nested generic collections, primitive/boxed types, enum/UUID/time types and invalid payloads; compare normalized diagnostics and runtime acceptance, not generated source text alone. |
| OS/process sandboxing | Runtime invariants are in-process controls, not a process sandbox. | Same limitation. | Document deployment recipes for gVisor/containers/Wasm isolation; keep sandbox lifecycle outside the semantic engine. |

## Step 71 implementation

- Harden the shared Docker metrics collector used by the .NET benchmark runners.
- Stop suppressing `docker compose ps` and `docker stats` failures.
- Write collector diagnostics beside the CSV and a machine-readable collector status file.
- Mark a zero-sample run as failed instead of publishing a plausible empty summary.
- Propagate summary failure from Run 1–4 orchestration scripts.
- Preserve the existing .NET and Java PostgreSQL `SKIP LOCKED` implementations; add no duplicate SQL path.

## Step 72 implementation — lossless durable audit adapter

- Added `.NET IAuditEventStore` and `BufferedDurableAuditLog` using a bounded `Channel` with `FullMode.Wait`, one sink writer, per-event acknowledgement and propagated sink failures.
- Added Java `AuditEventStore` and `BufferedDurableAuditLog` using a bounded `ArrayBlockingQueue`, one sink writer, per-event acknowledgement and propagated sink failures.
- Both adapters add an event to their process-local query view only after the sink acknowledges persistence.
- Both adapters drain accepted events during close/disposal; queue saturation applies producer backpressure instead of dropping events.
- The sink contract is deliberately abstract. A deployment must supply a genuinely durable implementation (for example a transactional database or append-only durable journal).
- Existing `ToolCallGovernor` methods still use synchronous `IAuditLog` and therefore wait for the durable acknowledgement when this adapter is configured. This avoids claiming non-blocking governance before async governor parity is implemented.
- Added matching tests for acknowledgement, sink failure, committed-query visibility and close/drain behaviour.


## Step 73 implementation — governance fail-closed telemetry

- Added matching point-in-time audit buffer metrics in .NET and Java: queue depth, accepted writes, persisted writes, failed writes, backpressure wait count and cumulative wait duration.
- Metrics are observational only; queue depth is approximate under concurrent producers and must not be used as a correctness condition.
- Extended both runtime adapter tests to assert accepted/persisted/failure counter behaviour.
- Verified the existing governance ordering: risk and policy audit writes happen before the final deny/approval/route branch; the allow-path `Routed` audit happens after route construction but before the allow result is returned. Since `IAuditLog.Record` propagates sink failures, no allow result is returned when required audit persistence fails.
- Approval creation precedes the `ApprovalRequested` audit. A sink failure at that point can leave an approval record created without returning it; a transactional approval+audit outbox is still required to make this transition atomic.
- No async governor API was introduced: synchronous `IAuditLog` is the established cross-runtime port, and its acknowledged durable adapter already provides backpressure without changing the governance API. A genuinely non-blocking async governance API needs an explicit API and cancellation/transaction contract rather than an adapter-only claim.

## Ordered follow-up work

1. **Telemetry and load evidence:** run identical benchmark tiers and concurrency levels for .NET and Java; capture CPU, memory, DB pool wait, request queue depth, p50/p95/p99 latency, error rates and collector health. Do not infer thread starvation from `SampleCount: 0` alone.
2. **Tenant-first planner invariant:** introduce one shared conformance scenario proving tenant scope is bound before schema/relationship discovery and survives every rewrite/provider boundary in both runtimes.
3. **Audit transactionality and recovery:** implement transactional approval+audit/outbox transitions, concrete durable sinks, process-crash/restart recovery tests, and only then design an async governor API with explicit cancellation and transaction semantics. Steps 72–73 supply acknowledged bounded adapters and operational counters, not a concrete durable sink or non-blocking governor.
4. **Safe structural plan cache:** cache only immutable, tenant-neutral compilation shapes. Bind all request-specific authorization and parameters after lookup. Include cache-key versioning, bounded eviction and cross-runtime canonicalization tests.
5. **Lease and plan pinning:** exchange a verified upstream identity for a short-lived, least-privilege lease; pin the immutable semantic plan digest; revalidate lease and current authorization at every execution boundary.
6. **Durable HITL:** suspend workflows without holding request threads or DB transactions; bind approval to plan digest, tenant, actor, target, expiry and one-time nonce; verify signatures before resumption.
7. **AOT/type conformance:** add shared test vectors and fuzzed invalid payloads for nullable and nested generic type edges, then execute .NET Native AOT and Java Native Image evidence independently.
8. **Sandbox deployment:** publish hardened deployment recipes and threat boundaries for gVisor/container/Wasm runtimes.

## Release rules

- No claim of parity from matching generated text alone.
- No claim of snapshot consistency from a cursor fingerprint alone.
- No claim of audit durability from an in-memory or asynchronously queued implementation without a documented flush/acknowledgement contract.
- No cache may preserve an authorization decision across requests.
- No benchmark report is valid when required telemetry has zero samples or collector diagnostics are missing.
- Every item is marked implemented, implemented-unverified, or certified only with its actual build/runtime evidence.

## Step 74 — atomic approval/evidence boundary (contract and fail-closed enforcement)

Implemented in both runtimes:
- Added `IAtomicFoundgineGatewayApprovalStore` and matching Java interface.
- Approval creation now requires this stronger store contract. A legacy store that only implements ordinary approval persistence is rejected before a receipt/handle is returned.
- The atomic operation receives non-secret `ApprovalRequested` evidence (event id, subject, tenant, plan fingerprint, occurrence and expiry timestamps) alongside the approval payload. Implementations must commit approval state and its audit/outbox record in one database transaction and must not expose the approval if either write fails.
- The in-memory development stores implement the contract under one synchronization boundary and expose evidence for tests/diagnostics. They remain process-local and non-durable; they are not production transactional outbox implementations.

Important boundary: this step establishes and enforces the storage contract, not a PostgreSQL implementation. The repository does not yet contain a durable serializer/schema/transaction adapter for `MutationPlanApproval` plus `foundgine_outbox`. Production multi-instance approval workflows must supply an implementation that enlists both writes in the same PostgreSQL transaction. The next step is the durable PostgreSQL implementation and restart/failure-injection integration tests. A separately committed `EnqueueAsync` call is explicitly insufficient.

## Step 75 — PostgreSQL transactional approval store (implemented-unverified)

- Added `PostgresFoundgineGatewayApprovalStore` in .NET and Java. Both insert the approval row and its `ApprovalRequested` outbox row using the same PostgreSQL connection and transaction; either both commit or both roll back.
- Both implementations reject standalone `Store`/`store` writes. The production adapter cannot accidentally persist an approval without its evidence row.
- Added append-only migration `004_gateway_approvals.sql` in both runtimes and registered it with both event-store migrators. The .NET event-store project now embeds the complete migration set rather than only `001_initial.sql`.
- Approval consumption uses a conditional `DELETE ... RETURNING` bound to handle digest, subject, nullable tenant and expiry. The delete is committed only after the host codec successfully decodes the approval, so a codec failure rolls back consumption. Concurrent consumers therefore cannot both receive the same approval.
- Added an explicit `FoundgineGatewayApprovalPayloadCodec` port in each runtime. The host owns an AOT/native-image-compatible codec and should encrypt the approval payload at rest; the adapter does not silently choose a reflection-based serializer or invent key management.
- The outbox `tenant_id` is non-null by existing schema contract. Global-scope approval events use the reserved routing partition `__foundgine_global__`; the event payload retains the actual nullable tenant. Deployments must reserve that synthetic partition identifier and must not use it as a real tenant id.
- The database adapter has not yet been exercised against PostgreSQL in this environment. Added source-contract checks do not substitute for rollback, concurrent-consumer, restart or failure-injection integration tests. Marked implemented-unverified pending those tests and .NET/Java builds.

## Step 76 — PostgreSQL approval-store integration suites and audit hardening

Evidence status: **Java verified** against a live PostgreSQL 16 server (20 integration tests plus 12 existing gateway approval tests passing; seven injected mutants each caught by the intended tests). **.NET implemented-unverified**: the .NET SDK and NuGet were unavailable in the audit environment, so the matching .NET suite and store changes were reviewed, not compiled or run. Run `dotnet test src/csharp/tests/Foundgine.EventStore.Tests` (Docker required) before certifying.

Added:
- .NET `PostgresGatewayApprovalStoreIntegrationTests` (Testcontainers) and Java `PostgresFoundgineGatewayApprovalStoreIntegrationTest` (opt-in via `FOUNDGINE_TEST_PG_URL`, isolated schema, dropped afterwards). Scenarios: atomic commit, rollback on outbox failure, duplicate-digest failure, codec encode/decode failure recovery, concurrent one-time consumption (also across independent store instances), restart consumption, subject/tenant isolation, expiry boundary, revocation, global-partition handling, outbox leasability by the real `PostgresOutbox`.
- Conformance test `test_postgres_approval_integration_parity.py` enforcing that every Java scenario has a .NET counterpart.
- CI job running both integration suites against PostgreSQL.

Defects found by the audit and fixed in both runtimes:
1. The outbox `message_key` was the handle digest, leaking the store-level lookup credential to brokers and dead-letter tooling. It is now the event id.
2. A real tenant named `__foundgine_global__` would have shared the global outbox partition. It is now rejected.
3. Digest and event id were not validated before reaching the database (Java accepted any `UUID.fromString` form). Both are now strict hex; lookups with malformed digests return no row without querying.
4. .NET passed caller `DateTimeOffset` values with non-zero offsets to Npgsql, which rejects them for `timestamptz`. They are normalized to UTC.
5. Java emitted dashed event ids while .NET emitted `N` format. Java now emits `N` format.
6. Expired approvals were never removed. Added `purgeExpired` / `PurgeExpiredAsync` (batched, `SKIP LOCKED`); hosts must schedule it.

Residual risks and open items:
- `take` decodes inside the transaction, so a slow codec (for example a KMS call) holds a row lock and a pooled connection. Concurrent consumers of the same handle block until it finishes. Keep codecs fast or add a lock timeout.
- Consumption commits before mutation execution: a crash or failure after `take` loses the approval (at-most-once). This is the safe direction but must be documented to operators.
- Expiry uses the application clock passed to `take`, not the database clock; multi-node clock skew shifts the effective expiry.
- Approval payloads are plaintext JSON unless the host codec encrypts them; no production codec ships.
- The evidence JSON casing is codec-defined, so .NET and Java hosts may emit different property casing.
- The Python source-contract tests are keyword checks and are not evidence of behavior; only the integration suites are.

## Step 77 — approval payload encryption at rest: .NET parity, shared vectors (Java verified, .NET implemented-unverified)

Finding: Java already shipped `EncryptingFoundgineGatewayApprovalPayloadCodec`, `ApprovalPayloadEnvelope` and `FoundgineGatewayApprovalKeyProvider` (AES-256-GCM, key-id rotation) and its documentation claimed the format was shared byte-for-byte with .NET, but .NET had no counterpart and neither runtime had tests. The Step 76 residual-risk line "no production codec ships" is therefore only half true: an encrypting decorator ships in Java and now in .NET; the inner AOT-safe serializer and the key store remain host-supplied.

Added:
- .NET `IFoundgineGatewayApprovalKeyProvider`, internal `ApprovalPayloadEnvelope` and `EncryptingFoundgineGatewayApprovalPayloadCodec` using `AesGcm` (16-byte tag). Wire format and AAD are identical to Java: `{"v":1,"kid":..,"n":..,"c":..}`, AAD `foundgine.approval.v1|<kid>`. The shape parser is anchored with `\A…\z` so a trailing newline is rejected (Java `matches()` is already full-string).
- `src/conformance/approval-envelope-vectors.json`, generated by the independent Python reference `tools/generate_approval_envelope_vectors.py` (3 seal vectors with fixed nonces, 12 reject vectors: tampered ciphertext, relabelled key id, unknown key, version 2, whitespace, trailing whitespace, extra field, reordered fields, short nonce, empty ciphertext, non-JSON, raw plaintext).
- Java `ApprovalPayloadEnvelopeConformanceTest` and .NET `ApprovalPayloadEnvelopeConformanceTests` (same scenario names, enforced by a Python parity test), plus random-nonce, key-rotation and invalid-key-material cases.
- `test_approval_envelope_conformance.py` and CI wiring (`cryptography` added to the contract-test job; vector and Gateway test paths added as triggers).

Evidence:
- Verified here: the vector file regenerates byte-identically; every seal vector decrypts and every reject vector fails under an independent implementation (Python `cryptography`); the real Java `ApprovalPayloadEnvelope` class, compiled with the JDK, reproduces all 3 seal vectors, opens them, and rejects all 12 reject vectors; the Python parity tests pass.
- Not verified here: the JUnit test class itself (no Maven or JUnit jars offline) and everything in .NET (no SDK). Run `mvn -pl foundgine-providers test -Dtest=ApprovalPayloadEnvelopeConformanceTest` and `dotnet test src/csharp/tests/Foundgine.Gateway.Tests` before certifying.

Known limits (unchanged by this step):
- The codec port still does not receive subject, tenant or digest, so the ciphertext is not cryptographically bound to the row; the store predicates are the binding. Changing the port to pass an AAD context is a candidate follow-up (it breaks the envelope version and needs a v2).
- Base64 decoding is not canonical-only; GCM authenticates the decoded bytes, so this is not an integrity gap, but non-canonical encodings of the same bytes are accepted in both runtimes.
- A key provider backed by a KMS inherits the `take` lock-hold risk from Step 76 (decode runs inside the transaction).

## Step 78 — tenant-first gate before schema/relationship discovery (Java verified with javac/JUnit; .NET build-checked with a shim, real suite unrun)

Finding: in the read engines, `DryRun`, `ExecuteAsync(SemanticRequest)`, `ExecuteApprovedAsync` and `ExecuteAsync(ReadIntent)` resolved the request against the semantic model (`SemanticRequestResolver`, `ReadIntentCompiler`) *before* any warrant was verified. Resolver errors name entities, fields and relationships, so an unauthenticated or wrong-tenant caller could probe the schema. `ValidateWarrant` ran only after resolution, and tenant scope reached only the per-capability composition check. The mutation engines already verify the warrant before planning and were not changed.

Changed (C# `FoundgineEngine.AuthenticateBeforeDiscovery`, Java `authenticateBeforeDiscovery`): when a security context is present, the warrant is verified (signature, issuer, audience, expiry) and the warrant subject and tenant constraint are checked against the context before any name resolution. Denial is a generic `UnauthorizedAccessException` / `SecurityException` with no schema detail. `ValidateWarrant` still re-verifies after resolution as defense in depth.

Added tests (same scenario names in both runtimes; parity enforced by the Python test): `TenantFirstGateTests` (.NET, E2E) and `TenantFirstGateParityTest` (Java, runtime). Nine scenarios in `src/conformance/tenant-first-gate-scenarios.json`: wrong-tenant, missing-tenant and wrong-subject probes of an unknown entity get the generic denial with no entity name; a warrant signed with the wrong key never reaches the resolver; a same-tenant caller passes the gate and reaches the resolver; the `ReadIntent` and `ExecuteApprovedAsync` paths are gated before name resolution; a valid same-tenant request against a real entity plans and executes (provider runs once); a wrong-tenant caller on a real entity is denied before the provider runs (zero executions).

Evidence:
- Verified here: `test_tenant_first_gate_parity.py` (gate precedes every resolution site; scenario names match across runtimes). The test functions were run directly because pytest is not installed in this environment. It is a source-contract check, **not behavioral evidence**.
- Verified (Java): core and runtime main sources compile with `javac --release 21` (the POM targets release 27; only JDK 21 was available), and `TenantFirstGateParityTest` passes 9/9 under the JUnit console launcher, and the whole runtime suite passes 40/40. With the gate body disabled (mutant), 6 of the original 7 tests failed; the same-tenant-reaches-resolver test passes either way by design. Built with `javac` directly, not Maven, so POM and plugin behavior is untested.
- Verified (.NET, partially): with the .NET 10 SDK, `Foundgine.Core` builds with 0 errors, and `Foundgine.Runtime` sources (including the `FoundgineEngine` edit) build with 0 errors in a temporary project that excluded the five files needing `Microsoft.Extensions.DependencyInjection` (NuGet was unreachable) and enabled implicit usings/nullable on the command line, because the upload contains no `Directory.Build.props`. `TenantFirstGateTests` was compiled against a minimal hand-written xunit shim and a reflection runner, not real xunit: 9/9 pass. With the gate disabled the denial tests fail and the resolver's unknown-entity error leaks, as intended. The real `dotnet test` project, the DI extension files and every other .NET suite remain unrun.
- Still to run before certifying: `mvn -pl foundgine-runtime test` (real Maven, release 27) and `dotnet test src/csharp/tests/Foundgine.E2E.Tests --filter TenantFirstGateTests` with real xunit, plus the full existing suites in both runtimes.
- CI: the workflow's path triggers now include both engines and both new test files, but the workflow still only runs the Python contract tests for this area; it does not run the Java or .NET engine suites.

Open items:
- Requests with no security context still reach the resolver; schema error text is exposed to unauthenticated callers by design of the policy-only path. Decide whether that path needs sanitized errors.
- The planner (`_planner.Plan`) and provider boundary still do not receive the tenant directly; the plan-cache partition carries it. Proving it survives every rewrite remains open.
- `src/conformance/tenant-first-gate-scenarios.json` is the shared scenario contract (ids, request path, warrant/context, expected outcome class, gate and resolver message texts). The Python test enforces that both runtimes implement exactly these scenarios by name and use these message texts. The tests do not yet *load* the file; their bodies are hand-mirrored, so an expectation change must be edited in three places.

### Step 78 additional defect (Java only): warrant-bearing requests failed after provider execution

Found by the positive-path test. `FoundgineEngine.executeAsync` hashes `MAPPER.writeValueAsString(request)` into the intent fingerprint after the provider returns. The mapper is a plain `ObjectMapper` with no `java.time` support, and a security-bearing request embeds a `SecurityWarrant` with `Instant` fields, so every successful execution of a warrant-bearing request threw `IllegalStateException: Unable to serialize semantic request for intent fingerprinting` and discarded the result. No existing Java test executed a warrant-bearing request. C# serializes the same context with `System.Text.Json` and was unaffected (its positive-path test passes).

Fix: for a request with a security context, the fingerprint source is the request serialized without the context plus `|authority:` and `authorityCachePartition()` (which embeds the full warrant digest, so the authority remains bound). Requests without a security context fingerprint exactly as before. Security-bearing Java intent fingerprints could not previously be produced, so no stored value changes. They are not byte-identical to .NET's, which was already true because the JSON shapes differ; do not compare intent fingerprints across runtimes.
