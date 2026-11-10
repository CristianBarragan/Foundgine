# Foundgine Gateway

The gateway is a transport-neutral facade inside the existing `Foundgine.Providers` package. It exposes a stable capability contract and a bounded in-process event stream; it does not replace Foundgine's semantic authorization or execution pipeline.

## AOT-first design

- HTTP routes use explicit `RequestDelegate` handlers.
- Gateway JSON uses `System.Text.Json` source-generated metadata (`FoundgineGatewayJsonContext`).
- Diagnostics use the .NET `ActivitySource` and `Meter` APIs directly. There is no OpenTelemetry SDK dependency in the provider package; the host can subscribe its OpenTelemetry SDK to `FoundgineGatewayDiagnostics.InstrumentationName`.
- No runtime reflection, dynamic code generation, or runtime type discovery is required by these gateway components.

For an ASP.NET Core host:

```csharp
builder.Services.AddFoundgineGateway(options =>
{
    options.AddCapability(new FoundgineGatewayCapability(
        "semantic.discovery", "Semantic discovery", "1.0", "semantic",
        "Discover available semantic capabilities.", ["discover"]));
});

var app = builder.Build();
app.MapFoundgineGateway();
```

## Observability

Subscribe the host's OpenTelemetry configuration to:

```csharp
FoundgineGatewayDiagnostics.ActivitySource
FoundgineGatewayDiagnostics.Meter
```

The gateway emits event-publish activities and low-cardinality event/hook metrics. It intentionally does not attach request payloads, SQL, credentials, user IDs, tenant IDs, or arbitrary subject strings to metric labels.

## Durable audit

Implement `IFoundgineGatewayAuditSink` using the application's durable append-only audit store, then register it:

```csharp
builder.Services.AddFoundgineGatewayAudit<MyDurableAuditSink>();
```

The audit hook is critical: the event is not sent to best-effort hooks or live subscribers until the sink accepts it. Sink failures fail the publishing operation. The gateway does not claim that an in-memory event stream is a durable audit log.

Audit implementations should persist a versioned event envelope, enforce integrity/retention policy, and apply data minimization. Do not store secrets or indiscriminately persist arbitrary attributes supplied by callers.

## Provider lifecycle

`IFoundgineGatewayProviderRegistry` supports thread-safe registration, health reporting, snapshots, and unregistration. Lifecycle changes are published as gateway events. The registry describes providers; it does not instantiate them, choose execution strategies, or grant execution authority.

## Event delivery semantics

The in-process SSE/event hub is bounded and best-effort. A slow subscriber can lose older events when its buffer fills. For durable replay or cross-process delivery, connect a broker through `IFoundgineGatewayHook`; use a critical hook only when broker acceptance must gate publication. This increment does not add a broker dependency.


## Native semantic capability bridge (increment 4)

When `AddFoundgineGateway()` is registered in the same host as `AddFoundgine(...)`, the gateway exposes:

- `GET /foundgine/semantic` — the canonical `SemanticCapabilityContract` produced by the existing `IFoundgine.DescribeCapabilityContract()` API, together with `IFoundgine.DescribeVersionSet()`.

This is intentionally an adapter, not a second metadata or authorization engine. The response is descriptive; every query or mutation must still be submitted to the existing Foundgine application API and pass its execution-time authorization checks. Protect this endpoint with the host's authentication/authorization policy (for example, an OIDC/Keycloak policy). Do not expose it publicly merely because it is a discovery endpoint: conditional authorization predicates and model topology can be sensitive.

The HTTP endpoint serializes the response through `FoundgineGatewayJsonContext`, so the semantic response is included in the source-generated JSON metadata used by Native AOT. No reflection-based serializer fallback is introduced.


## Native semantic execution bridge (increment 5)

`IFoundgineGatewayExecution` is the transport-neutral execution facade for other products. It delegates directly to the existing `IFoundgineExecutor` and `IFoundgineMutations` runtime APIs:

- `ExecuteReadAsync(ReadIntent, ...)` — provider-neutral read intent through the canonical query pipeline.
- `DryRunMutation(SemanticMutationOperationGraph)` — plan and authorize without provider execution.
- `ApproveMutation(SemanticMutationOperationGraph)` — creates the runtime's fingerprint-bound approval.
- `ExecuteMutationAsync(...)` — direct authorized mutation execution.
- `ExecuteApprovedMutationAsync(...)` — rebinds the approval to the current host security context; the runtime re-plans, re-authorizes, and verifies the plan fingerprint before execution.

Register `AddFoundgineGateway()` alongside Foundgine's runtime registrations and register a scoped `ISecurityExecutionContextProvider` in the host. The provider must construct the context from authenticated host identity and trusted warrant issuance/validation. The gateway rejects execution if no host context exists, and it overwrites any security context attached to a read intent or mutation request. Tenant, subject, audience, and warrant data must never be accepted from an untrusted request body.

Example application-side use:

```csharp
var gateway = httpContext.RequestServices.GetRequiredService<IFoundgineGatewayExecution>();
var rows = await gateway.ExecuteReadAsync(readIntent, cancellationToken: httpContext.RequestAborted);
```

The returned `MutationPlanApproval` is an in-process runtime object containing the original request and security context. Treat it as sensitive server-side state: do not serialize it to clients or place it in browser storage. A public approval workflow should issue an opaque handle backed by a protected server-side/distributed approval store, bind the approver to the authenticated subject, and enforce expiry/revocation. That handle store is deliberately not invented as an in-memory gateway feature in this increment.

Execution lifecycle events carry only a safe operation category and correlation ID, not request values or results. Start-event publication is fail-closed (including critical hook failures). Completion/failure notifications are best-effort so a post-commit notification failure cannot misreport a committed mutation as failed. These notifications are not a substitute for an authoritative execution audit record containing the runtime's authorization decision, plan fingerprint, and execution evidence.

The bridge is an in-process API in this increment; it does not yet expose mutation or read execution over HTTP. This avoids publishing a premature wire contract for polymorphic mutation graphs and execution results before their complete AOT serialization contract and opaque approval lifecycle are defined.

## HTTP semantic read endpoint (increment 6)

`POST /foundgine/read` accepts a bounded JSON `ReadIntent` wire DTO and calls `IFoundgineGatewayExecution.ExecuteReadAsync`. The endpoint requires the host's default authorization policy. The authenticated host's `ISecurityExecutionContextProvider` supplies the warrant-backed identity; any security fields in a body are rejected as unknown JSON properties. Request parsing is source-generated, rejects unknown properties, and applies request byte, selection, filter, nesting, and result-limit bounds. The default result limit is 100 rows and the maximum is 1,000; both are configurable through `FoundgineGatewayHttpOptions`. Gateway routes require the host's default authorization policy by default. A host may explicitly pass `requireAuthorization: false` to make discovery/events public, but the read execution route remains protected unconditionally. The gateway never deserializes a `SecurityExecutionContext` from the client.

The response is a stable, minimized `{ rows, pageInfo }` contract. It intentionally does not serialize internal `ExecutionEvidence`, receipts, or provider metadata. Value serialization uses explicit primitive/date/GUID/byte/dictionary/sequence handling and returns HTTP 501 for unsupported CLR values rather than invoking reflection-based fallback serialization. If your provider returns custom value types, add an explicit AOT-safe wire mapping before exposing those values.

### Sample and tests

- `Samples/Gateway.MinimalApi` shows ASP.NET Core JWT/OIDC (including Keycloak-compatible authority configuration), endpoint mapping, and the host integration points.
- `Gateway.Tests` covers source-generated request/response serialization and rejects caller-supplied security properties.

The sample intentionally leaves Foundgine runtime configuration and the trusted `ISecurityExecutionContextProvider` implementation to the host application; it must be wired to your actual warrant issuer/validator. Do not deploy the sample until those host-specific services are registered.

## Opaque mutation approval handles (increment 7)

`FoundgineGatewayApprovalService` now wraps Foundgine's native fingerprint-bound approval object in an opaque, random, one-time handle. The runtime approval itself is never returned to a client. The handle is 256 bits of cryptographic randomness; only its SHA-256 digest is used as the store key. Handles are bound to the host-established subject and tenant, expire (10 minutes by default; configurable up to 24 hours through the service constructor), can be revoked by their owner, and are atomically consumed once. A mismatched subject or tenant cannot consume or revoke another caller's handle. Execution still calls `IFoundgineGatewayExecution.ExecuteApprovedMutationAsync`, which rebinds the current security context and delegates re-planning, authorization and fingerprint validation to Foundgine.

The storage boundary is `IAtomicFoundgineGatewayApprovalStore` (which extends `IFoundgineGatewayApprovalStore`). Approval creation fails closed for legacy stores that do not implement the atomic contract. A production implementation must commit the approval row and its `ApprovalRequested` audit/outbox row in the same database transaction, in addition to atomic compare-and-remove semantics, encryption/access controls for approval payloads, expiry cleanup, and availability appropriate to the mutation workflow. Calling `IFoundgineGatewayOutbox.EnqueueAsync` separately is not an atomic substitute. `AddFoundgineGatewayInMemoryApprovalStore()` is explicitly for development or a single process only: it loses approvals on restart and is not shared across replicas. Do not log or persist the plaintext handle in browser local storage; treat it as a short-lived bearer secret and deliver it only over TLS.

Example server-side use (the graph is authored against the application's semantic model, not accepted as caller-supplied security material):

```csharp
var approvals = httpContext.RequestServices.GetRequiredService<FoundgineGatewayApprovalService>();
var receipt = await approvals.CreateAsync(graph, httpContext.RequestAborted);
// Return only receipt.Handle, receipt.ExpiresAt and receipt.PlanFingerprint to the caller.

var execution = await approvals.ExecuteAsync(receipt.Handle, httpContext.RequestAborted);
// Null means unknown, expired, revoked, already consumed, or not owned by this caller.
```

The approval service is available as an in-process API in this increment. Public HTTP mutation endpoints, durable execution-status lookup and public evidence projection are not exposed yet: the repository does not currently provide a generic durable execution-status store, and the semantic mutation graph still needs a deliberately bounded AOT wire contract. Do not serialize `MutationPlanApproval`, `MutationExecutionResult`, or arbitrary provider result objects directly. The next step is to define those transport contracts and status/evidence persistence without weakening the runtime authorization boundary.

### Increment 7 tests

`Gateway.Tests` now also covers owner/tenant binding, expiry, revocation, one-time consumption and concurrent consumption of approval handles. These tests are included but have not been executed in this environment because the .NET SDK is unavailable.


## HTTP mutation endpoints

`MapFoundgineGateway()` also maps authenticated mutation endpoints:

- `POST /foundgine/mutations/dry-run`
- `POST /foundgine/mutations/approvals`
- `POST /foundgine/mutations/approvals/{handle}/execute`
- `DELETE /foundgine/mutations/approvals/{handle}`

The wire contract maps a bounded subset (Create/Update/Delete/Upsert and field/AND/OR filters) into the canonical `SemanticMutationOperationGraph`. It does not accept caller-provided security, effects, provider plans or approval objects. Relationship filters and generated-value dependencies remain available through the in-process API, not this initial HTTP subset. All mutation endpoints require authorization and rely on the host's trusted security context provider. The in-memory approval store is development-only; production deployments need a distributed, protected store with atomic consumption.


## Execution status and replayable events (increment 9)

The gateway now exposes:

- `GET /foundgine/executions/{id}` — returns a minimized status projection for a read or approved mutation started by the authenticated owner.
- `GET /foundgine/events` — supports SSE replay using the standard `Last-Event-ID` header, or `?after={sequence}` for an initial cursor.

Successful read and approved-mutation executions return `X-Foundgine-Execution-Id`. The ID is a random 128-bit identifier. Status lookup additionally checks a digest of the trusted tenant/subject context; the digest is stored in the internal event journal and removed from public SSE payloads. Unknown and non-owned execution IDs both return 404. Public status exposes only operation category, start/completion timestamps, state, and a bounded outcome code; it does not expose request data, result rows, warrants, SQL, or plan internals.

`IFoundgineGatewayEventStore` is the persistence boundary. The default `InMemoryFoundgineGatewayEventStore` retains up to 10,000 events in this process, assigns monotonic sequence values, and supports replay/correlation lookup. It is intentionally **not durable across restarts and not shared between replicas**. Production applications must implement this interface using a durable ordered log or database/outbox and register it with `AddFoundgineGatewayEventStore<TStore>()`. A production implementation must make append atomic, preserve sequence ordering across instances/restarts, report the current high-water mark, bound reads, and make correlation lookup owner-safe through the gateway's internal ownership attribute.

Example registration:

```csharp
builder.Services.AddFoundgineGatewayEventStore<SqlFoundgineGatewayEventStore>();
builder.Services.AddFoundgineGateway();
```

`SqlFoundgineGatewayEventStore` above is an application-defined adapter, not a type shipped by Foundgine. No broker, database schema, or vendor SDK is imposed by the provider package. The sample host keeps the process-local default so it runs without infrastructure; replace it before production deployment.

SSE replay closes the registration/replay race by registering the live subscriber before reading retained events and de-duplicating by sequence. Live subscriber channels remain bounded and may drop messages under backpressure; clients should reconnect with `Last-Event-ID`. Replay is capped at 10,000 records per connection. A cursor greater than the store's current high-water mark is reset to the current journal generation (important after a process-local store restart). This is at-least-once-style replay within the configured store's retention, not a promise of end-to-end exactly-once delivery. If a durable store has retention gaps, hosts should expose operational monitoring and clients should perform a fresh status/snapshot read.

The execution journal is operational status, **not authoritative audit evidence**. It deliberately does not claim to prove authorization decisions or provider-side commit state. Keep the critical audit sink and provider/runtime evidence as the authoritative audit source. Start-event publication is fail-closed; completion/failure notification publication is best-effort so an event-store outage after a successful commit does not turn the HTTP response into a false failure. In that situation, status may remain `running`/incomplete and must be reconciled against authoritative audit evidence.

### Increment 9 tests

`GatewayEventJournalTests` covers monotonic sequence assignment, replay cursors, correlation lookup, bounded retention, and replay delivery through the event hub. These tests are included in `Gateway.Tests`; they have not been executed in this environment because the .NET SDK is unavailable.

## Event stream: audience, replay and resume (increment 10.3)

`GET /foundgine/events` is audience-scoped and provider-independent (it only uses `IFoundgineGatewayEventStore`).

- **Audience (fail closed).** Events carrying `owner.digest` reach only that principal; events carrying `tenant.digest` reach that tenant; `CapabilityChanged` and `SemanticModelChanged` reach every authenticated caller; every other unscoped event (provider lifecycle/health, authorization denials, lifecycle events published by the execution facade, custom events) is a *system* event delivered only to callers satisfying `EventStream.OperatorPolicy`. With no policy configured, nobody sees system events. Operators never see other tenants' scoped events. Reserved attributes are stripped from the wire. The caller needs a trusted `SecurityExecutionContext` (otherwise 403).
- **Cursor.** No `Last-Event-ID`/`after` ⇒ live events only. `?after=0` replays from the start if the journal is within `EventStream.MaxReplayWindow` (default 10,000 sequences); otherwise `410 Gone`. Every response carries `X-Foundgine-Latest-Sequence`; on 410 take a fresh snapshot and reconnect with that value. Heartbeats (default 15 s) emit `: keepalive` and an `id:` checkpoint past events the caller cannot see.
- **Gap-free delivery.** Replay is paged (`ReplayPageSize`) and unbounded; events dropped by a full subscriber buffer, or published during a long replay, are back-filled from the journal before delivery. Journals should have gap-free sequences (the in-memory and PostgreSQL journals do); a gap costs one extra read.
- **Digest key.** `EventStream.SetScopeDigestKey(32+ bytes)` switches scope digests to HMAC-SHA-256 (same key on all replicas). Without it, owner digests use the legacy unkeyed format. Changing the key stops older events from matching their owners (execution status for older executions returns 404).
- This supersedes the earlier statements that replay is capped at 10,000 records per connection and that a connection without a cursor replays retained history.


### Atomic approval evidence (increment 11)

The .NET and Java approval services now require `IAtomicFoundgineGatewayApprovalStore` / `IAtomicFoundgineGatewayApprovalStore` before issuing a receipt. The atomic write receives non-secret evidence bound to the approved plan fingerprint, subject, tenant, and expiry. Durable implementations must insert both approval state and the corresponding outbox/audit event on the same transaction/connection; a failure in either insert must roll back both. The in-memory implementations model the combined operation under one process-local synchronization boundary and expose evidence for tests, but remain non-durable and unsuitable for multi-instance production. Step 75 adds `PostgresFoundgineGatewayApprovalStore` in both runtimes. The .NET host can register it with `AddFoundgineGatewayPostgresApprovalStore<TCodec>()` after registering `NpgsqlDataSource` and its AOT-safe `IFoundgineGatewayApprovalPayloadCodec`; Java hosts construct the adapter with their `DataSource` and `FoundgineGatewayApprovalPayloadCodec`. The codec must produce valid JSON for the `jsonb` columns and should encrypt approval payloads at rest. Apply migration `004_gateway_approvals` before enabling the adapter. The reserved outbox routing tenant `__foundgine_global__` must not be used as a real tenant identifier.
