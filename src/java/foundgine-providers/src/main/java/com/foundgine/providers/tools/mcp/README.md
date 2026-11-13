# com.foundgine.providers.tools.mcp

Foundgine's Model Context Protocol integration for Java. It is the port of the C# package `Foundgine.Providers.Tools.MCP`: same
tool names, same security model, same semantics.

## What is in this package

- `FoundgineMcpServiceCollectionExtensions` — registration (`addFoundgineMcp`, the port of `AddFoundgineMcp`).
- `FoundgineMcpTools` — `foundgine_capabilities`, `foundgine_query`, `foundgine_query_semantic`.
- `FoundgineMcpMutationTools` — `foundgine_mutation_dry_run`, `foundgine_mutation_approve`, `foundgine_mutation_execute_approved`.
- `MutationJsonAdapter` — the mutation JSON shape the mutation tools accept.
- `FoundgineMcpServer` — a transport-neutral JSON-RPC 2.0 core (`initialize`, `ping`, `tools/list`, `tools/call`). The C# adapter
  gets this layer from the ModelContextProtocol SDK; Java hosts get it here without an extra dependency. The host owns the
  HTTP/stdio transport.
- `client/FoundgineMcpAgentClient` — capability discovery and dynamic intent execution from an MCP client.

## Security model (identical to C#)

Discovery is not authorization. The caller's identity, tenant, audience and warrant come **only** from the host through an
`ISecurityExecutionContextProvider` (or a `Supplier<SecurityExecutionContext>`; supplying both is rejected).

- Every tool requires a host context before it parses or executes anything, and fails closed with a `SecurityException` without one.
- A `security` block (or `tenantId`, `subject`, `warrant`, `sql`, `connectionString`, ...) inside an agent's intent is rejected,
  and the host context overwrites whatever the parsed intent carried.
- `approve` records the authenticated subject as the approver. The agent's `approvedBy` is ignored.
- `execute_approved` re-binds the host context and the engine re-checks the exact plan fingerprint.
- Register `FoundgineMcpServer` without `withMutationTools(...)` for agents that must only read.
- The provider is called synchronously when a tool is invoked, so thread-bound hosts (servlet filters) work.

## Usage

```java
var tools = FoundgineMcpTools.builder(foundgine)
        .securityContextProvider(() -> currentRequestContext())   // host-owned, never from the agent
        .lexicalGrounder(grounder)                                // optional: natural-language foundgine_query
        .build();
var mutations = new FoundgineMcpMutationTools(foundgineMutations, null, () -> currentRequestContext());

var server = FoundgineMcpServer.builder()
        .withReadTools(tools)
        .withMutationTools(mutations)
        .build();

// in your HTTP handler: POST body -> reply (empty Optional => answer 202 with no body)
server.handleAsync(body).thenAccept(reply -> reply.ifPresent(this::writeJson));
```

With a registry instead: `FoundgineMcpServiceCollectionExtensions.addFoundgineMcp(registry, contextFactory, provider, null)`.

## Client workflow

```text
MCP discovery -> capabilities -> provider-neutral intent -> semantic resolution -> authorization -> execution
```

## Differences from the C# adapter

| Area | C# | Java |
| --- | --- | --- |
| Tool plumbing | `[McpServerTool]` attributes + SDK | `FoundgineMcpServer` registers the same names and descriptions |
| Approval token | the serialized `MutationPlanApproval` record | opaque JSON `{approvalId, planFingerprint, approvedBy, approvedAt, request}`; the engine re-verifies the fingerprint either way |
| Errors shown to agents | SDK shows a generic message | security, validation (`IllegalArgumentException`) and not-configured messages are shown; anything else is generic |
| Malformed intent | `InvalidOperationException` | surfaced as `IllegalArgumentException` with the same message |
| `null` filter on update | throws | treated as no filter |
| Async failures | faulted `Task` | failed `CompletionStage` (`describeCapabilities` throws synchronously, like C#) |

The deprecated single-argument `FoundgineMcpTools(Executor)` / `FoundgineMcpMutationTools(Executor)` constructors are pass-through
shims kept for existing hosts; they do **not** enforce the security boundary.

Requires `jackson-databind` and `jackson-datatype-jsr310` (receipts carry timestamps).

## Cancellation, Spring and LangChain4j

Verification status: the MCP package, its 50 tests (including the real-engine approval round trip) and the LangChain4j adapter
were compiled with javac 21 against the real core/runtime sources, Jackson 2.15.2 and LangChain4j 1.0.0 sources and run with a
JUnit shim. Not verified: the Maven build (Jackson 2.18.9, JUnit 5) and the Spring module, which has never been compiled.

- `FoundgineMcpServer` handles `notifications/cancelled` (the in-flight `tools/call` token is cancelled and, per the MCP spec,
  no response is sent) and `handleAsync(body, transportToken)` lets the host cancel on client disconnect.
- `foundgine-spring`: `FoundgineMcpAutoConfiguration` (off unless `foundgine.mcp.enabled=true`; requires `IFoundgine` and an
  `ISecurityExecutionContextProvider`; mutations also need `foundgine.mcp.mutations-enabled=true`) and an optional POST endpoint
  with an `Origin` allow-list (`foundgine.mcp.allowed-origins`).
- `foundgine-langchain4j`: `FoundgineLangChain4jTools.toolMap(server)` for `AiServices.tools(...)`.
- Both new modules are outside the root reactor, like `foundgine-spring` was; add them to `<modules>` once built.

## Portable approval-token wire contract (v1)

`FoundgineMcpApprovalToken` defines a language-neutral signed envelope for the approval boundary. The wire value is
`fgmcp1.{payload-base64url}.{signature-base64url}`. The signature is RSA PKCS#1 v1.5 with SHA-256 over the exact
payload bytes. The payload is UTF-8 with seven newline-separated fields: version (`1`), base64url UTF-8 key id,
base64url approval id, base64url plan fingerprint, base64url authenticated approver, Unix epoch milliseconds,
and base64url UTF-8 original mutation-request JSON. Base64url is unpadded. Text fields are encoded separately so
newline characters and serializer-specific JSON escaping cannot alter field boundaries.

The codec is deliberately key-provider agnostic: hosts own private-key custody and public-key trust. Unknown keys,
malformed versions, invalid encodings, and invalid signatures fail closed. The request JSON remains opaque signed
bytes; after verification, the MCP adapter must parse it, bind the current host security context, and let the mutation
engine re-authorize and compare the plan fingerprint. Signing an approval does not replace authorization, replay
protection, expiry policy, or the engine's execute-time checks.

**Integration status:** this checkpoint adds the shared codec primitives and Java tamper test. The existing MCP
`approve` / `execute-approved` methods still use their prior record-serialization path until host key configuration
is threaded through both adapters. Do not treat the old JSON approval object as a signed token.

## Approval-token lifetime and replay protection

Signed approval tokens are now subject to an explicit host-owned policy before execution. Supply
`FoundgineMcpApprovalTokenPolicy` with a positive maximum age (default recommendation: five minutes),
a bounded future-clock skew (default recommendation: 30 seconds), and an atomic replay store. The
store's `tryConsume(approvalId, expiresAtUnixMilliseconds)` operation must be atomic across every
application instance and retain the identifier until expiry. A process-local set is suitable only
for tests or a single-process deployment; clustered deployments should use a shared transactional
store (for example, a database uniqueness constraint or Redis SET NX with expiry).

Execution fails closed when the policy is not configured. Signature verification, request parsing,
and host security-context acquisition occur before the approval id is consumed; the id is consumed
immediately before the mutation engine is invoked. A consumed token cannot be retried after an
execution failure, which is the deliberate at-most-once security trade-off. Warrant replay protection
remains independently enforced by the mutation engine.

The shared golden-vector fixture contains a private key explicitly marked `privateKeyPem_TEST_ONLY`.
It exists solely to prove deterministic RSA PKCS#1 v1.5/SHA-256 signing parity in tests. Never use
that key in a deployed application.

## Approval-token key rotation

`FoundgineMcpApprovalKeyRing` is both the `Signer` and the `KeyResolver`; it matches the C# `FoundgineMcpApprovalKeyRing`.
It signs with one active key and keeps older public keys verifiable so outstanding approvals survive a rotation.

```java
var policy = FoundgineMcpApprovalTokenPolicy.defaults(replayStore);
var ring = FoundgineMcpApprovalKeyRing.forPolicy(policy);   // minimum retention = max age + skew
ring.activateSigningKey("2026-11", currentPrivate, currentPublic);
ring.activateSigningKey("2026-12", nextPrivate, nextPublic, policy.minimumKeyRetention());
addFoundgineMcp(services, ctx, provider, null, ring, ring, policy);
```

- Key ids are never reused, even after expiry or `revoke`; `revoke` is immediate (compromise response).
- Verify-only instances use `addVerificationKey` and `retireAfter` (which can only shorten a deadline).
- `encode` signs through `Signer.snapshot()`, so a rotation cannot split key id from signature.
- RSA keys must be at least 2048 bits. The wire format and the golden vector are unchanged.

## MCP session lifecycle

`FoundgineMcpSessionManager` (C#: same name) issues opaque `Mcp-Session-Id` values (256 random bits, base64url), slides an
idle timeout, enforces an absolute lifetime and a session cap, validates, and terminates on request. The store keeps only
a digest of the id (`Store`; `InMemoryStore` is for single-instance hosts, clusters need a shared store).

**A session is not authentication.** Authenticate every request first; a valid session never yields a security context,
approval or warrant. Bind it to the authenticated subject with `create(owner)` and pass the same owner to
`validate`/`terminate`. Answer every non-`VALID` status with the same HTTP 404. Call `purgeExpired()` periodically.
The Spring controller uses the gate when `foundgine.mcp.sessions-enabled=true`.

## MCP SSE replay (spec `mcp-sse-replay-v1`)

`FoundgineMcpSseBuffer` (C#: same name) is a bounded per-session event log. Event ids are the decimal sequence (from 1, never
reused). `replayAfter(Last-Event-ID)` returns the retained events after that id; a missing id yields none, an unparseable or
future id is `INVALID`, and an id older than the retained window is `EVICTED` (events were lost: the client must
resynchronise, never silently skipped). Only space and tab are trimmed from the header. `format` writes one `id:` line and one
`data:` line per payload line, so embedded newlines cannot inject fields. `FoundgineMcpSseRegistry` holds one buffer per
session (LRU-bounded, `remove` on termination). `FoundgineMcpSessionGate.admitGet` admits the GET (400 missing, 404 otherwise);
only then may the transport touch a buffer. In-process only.

**Live stream.** `FoundgineMcpSseBuffer.subscribe` hands out one wake-up slot per session (a second GET is refused: BUSY).
`FoundgineMcpSseStream` (Java) drives a connection through a `Sink` and an `Executor`: it keeps a cursor and, on every nudge,
writes `replayAfter(cursor)`, so replay and live events are one path (ordered, exactly once, no gap). A client that falls so far
behind that unsent events are evicted is closed, not skipped; a write failure closes the stream; `heartbeat()` writes a
`: keep-alive` comment. `FoundgineMcpSsePublisher.publish(sessionId, message)` is how host code sends a notification, server
request or response: it must be a single JSON-RPC 2.0 object (batches refused), is re-serialised to one line, and is dropped
(empty result) for a session with no buffer. The Spring controller creates the buffer when the session is issued and removes it,
closing the live stream, on DELETE.

**Lifecycle.** A buffer is *retired* (`retire()`) whenever it leaves the registry: evicted as least recently used, `remove`d on
DELETE, or dropped by `registry.sweep(isLive)`. Retiring tells the holder of the live-stream slot (`onRetire`, outside the
lock), frees the slot for good (a later `subscribe` returns null; the stream layer reports `GONE`) and leaves the retained events
readable. `FoundgineMcpSessionManager.isLive` is the non-sliding, owner-agnostic liveness check used by the sweep (never use it to
admit a request); the session store is keyed by digest, so only this check can tell which buffers belong to expired sessions.

**Limits and audit.** `FoundgineMcpSseLimits` caps open streams in total and per principal (sessions without an owner share one
bucket); a permit is released on every close and on every refused open. A stream older than `maxLifetimeMillis` is closed at its
next heartbeat (the client reconnects with `Last-Event-ID`; an open stream does not extend the session's idle deadline). The
stream emits `SSE_OPENED` (`resume=true|false`), `SSE_REJECTED` (`status=BUSY|EVICTED|INVALID|LIMIT|GONE`) and `SSE_CLOSED`
(`reason=client_closed|write_failed|evicted|session_ended|lifetime|shutdown`, only the first close is recorded) with the session
fingerprint and principal, never the session id, a payload or a `Last-Event-ID`.

Spring statuses for GET: 400 invalid `Last-Event-ID`, 404 the session ended meanwhile, 409 a stream is already open, 410 the id
was evicted (resynchronise), 429 a stream limit is reached, 405 SSE not enabled. Properties: `foundgine.mcp.sse-enabled=true`;
`sse-heartbeat-seconds` (25); `sse-max-stream-seconds` (3600, 0 = no limit); `sse-sweep-seconds` (60: purges expired sessions
and drops their buffers); `sse-max-open-streams` (5000); `sse-max-streams-per-principal` (10). A `FoundgineMcpAuditSink` bean
receives the events. The controller's `SseOptions` record carries the same settings.
C#: the buffer, registry, manager and gate have the same behaviour (`Subscribe(onAppend, onRetire)`, `Retire`, `Sweep`, `IsLive`,
`SseOpened/SseRejected/SseClosed` audit types), and `FoundgineMcpSseStream` / `FoundgineMcpSseLimits` mirror the Java classes
(`Open(buffer, lastEventId, sink, executor, options)` with an `IFoundgineMcpSseSink` and an `Action<Action>` executor).
`FoundgineMcpSsePublisher.Publish` returns null for a session with no buffer. ASP.NET: `services.AddFoundgineMcpSessions();
services.AddFoundgineMcpSse(options)` registers the registry, publisher, `FoundgineMcpSseEndpointOptions` and the
`FoundgineMcpSseSweeper` hosted service (purge + sweep every `SweepSeconds`); `app.MapFoundgineMcpSse("/mcp")` maps GET (the live
stream: same statuses as Spring, 400/403/404/405-by-absence/409/410/429) and DELETE. Frames reach the response through a bounded
queue per client, so a slow client is closed (`write_failed`) instead of blocking a publisher. The host keeps POST (it is
typically the ModelContextProtocol SDK): call `gate.AdmitPost`, and `registry.Stream(issuedId)` for a newly issued session.
Authenticate before the endpoints; the principal is `HttpContext.User.Identity.Name`. Unverified: written without a .NET SDK.

**Transport and conformance.** `FoundgineMcpSseTransport` holds the HTTP decisions (origin 403, gate 400/404, then the stream
result 400/409/410/429/404 or 200; DELETE 204) free of Spring, and `FoundgineMcpController` is a thin adapter over it. The C#
`FoundgineMcpSseTransport` is the same logic. Both runtimes run `src/conformance/mcp-sse-scenarios.json` (buffer) and
`mcp-sse-transport-scenarios.json` (statuses). Single instance only: the registry, buffers, limits and sweep are in-process, so a
cluster needs a shared event log and shared limits. The `foundgine.mcp.*` properties are described in
`META-INF/additional-spring-configuration-metadata.json` of `foundgine-spring`.
