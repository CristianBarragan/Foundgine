# Foundgine.Providers.Tools.MCP

`Foundgine.Providers.Tools.MCP` is Foundgine's Model Context Protocol integration.

## What is in this package

- `FoundgineMcpServiceCollectionExtensions` — MCP service registration.
- `FoundgineMcpTools` — read/capability tools exposed to an MCP client.
- `FoundgineMcpMutationTools` — mutation tools using the Foundgine mutation boundary.
- `FoundgineMcpAgentClient` — capability discovery and dynamic intent execution from an MCP client.
- MCP request/response/error handling used by the adapter.

## Capability discovery

The MCP surface can expose the semantic capabilities available to the current host/application. Discovery is not authorization: the host still supplies the security execution context, and Foundgine evaluates authorization before execution.

## Client workflow

```text
MCP discovery
    ↓
capabilities
    ↓
provider-neutral intent
    ↓
Foundgine semantic resolution
    ↓
authorization
    ↓
execution
```

## Install

```bash
dotnet add package Foundgine.Providers.Tools.MCP
```

Use this package when Foundgine capabilities need to be exposed or consumed through MCP.

## Signed mutation approvals

The MCP approve/execute tools use the portable `fgmcp1` signed-token format shared with Java. Register both an
`IFoundgineMcpApprovalTokenSigner` and an `IFoundgineMcpApprovalTokenKeyResolver` (or pass both to
`AddFoundgineMcp`). The wire signature is RSA PKCS#1 v1.5 with SHA-256 over the exact UTF-8 payload bytes.
Execution verifies the signature before parsing the signed mutation JSON, binds the current host security context,
and delegates authorization and exact plan-fingerprint revalidation to the mutation engine. Unsigned legacy JSON
approval records are rejected. Expiry and replay are enforced by `FoundgineMcpApprovalTokenPolicy`; hosts own the replay store and private-key custody.

## Approval-token key rotation

`FoundgineMcpApprovalKeyRing` is both the signer and the key resolver. It signs with one active key and keeps older
*public* keys verifiable so outstanding approvals survive a rotation:

```csharp
var policy = new FoundgineMcpApprovalTokenPolicy(replayStore);
var ring = FoundgineMcpApprovalKeyRing.ForPolicy(policy);          // minimum retention = max age + skew
ring.ActivateSigningKey("2026-11", currentRsaKey);
// later: new key becomes the signer, the old one stays verifiable for the retention
ring.ActivateSigningKey("2026-12", nextRsaKey, policy.MinimumKeyRetention);
services.AddFoundgineMcp(approvalTokenSigner: ring, approvalTokenKeys: ring, approvalTokenPolicy: policy);
```

- Key ids are never reused, even after expiry or `Revoke`. `Revoke` is immediate and is the compromise response.
- Verify-only instances use `AddVerificationKey` and `RetireAfter` (which can only shorten a deadline).
- A rotation cannot split key id from signature: `Encode` signs through `ring.Snapshot()`.
- RSA keys must be at least 2048 bits. The wire format and the golden vector are unchanged.

## MCP session lifecycle

`FoundgineMcpSessionManager` issues opaque `Mcp-Session-Id` values (256 random bits, base64url), slides an idle timeout,
enforces an absolute lifetime and a session cap, validates, and terminates on request. The store holds only a digest of
the id (`IFoundgineMcpSessionStore`; the in-memory store is for single-instance hosts, clusters need a shared one).

**A session is not authentication.** Authenticate every request first; a valid session never yields a security context,
approval or warrant. Bind a session to the authenticated subject with `Create(owner)` and pass the same owner to
`Validate`/`Terminate`. Answer every non-`Valid` status with the same HTTP 404. Call `PurgeExpired()` periodically.


## MCP SSE: server-to-client messages

With sessions on, a client can open a long-lived `GET` on the MCP endpoint to receive server-initiated messages
(notifications, server requests). Each session has a bounded replay buffer, so a client that reconnects with
`Last-Event-ID` gets what it missed. The behaviour is the same as the Java adapter (see its README for the buffer, stream and
audit details) and both runtimes run the shared scenarios in `src/conformance/` (`mcp-sse-scenarios.json` for the buffer,
`mcp-sse-transport-scenarios.json` for the HTTP statuses).

```csharp
services.AddFoundgineMcpSessions();
services.AddFoundgineMcpSse(new FoundgineMcpSseEndpointOptions(MaxStreamSeconds: 3600), maxEvents: 256);
// or from configuration (nothing is registered unless Enabled is true):
services.AddFoundgineMcpSse(configuration.GetSection("Foundgine:Mcp:Sse"));

app.MapFoundgineMcpSse("/mcp").RequireAuthorization();   // GET = live stream, DELETE = end the session
publisher.Publish(sessionId, jsonRpcMessage);            // FoundgineMcpSsePublisher; null if the session has no buffer
```

**Configuration keys** (the Java `foundgine.mcp.sse-*` properties, same defaults): `Enabled` (false), `HeartbeatSeconds` (25),
`MaxStreamSeconds` (3600, 0 = no limit), `SweepSeconds` (60), `MaxOpenStreams` (5000), `MaxStreamsPerPrincipal` (10),
`MaxStreams` (10000 sessions with a buffer), `MaxEvents` (256), `MaxBytes` (1048576) and `AllowedOrigins` (a list). A value that
is not a number fails at startup.

**GET statuses**, checked in this order: 403 origin refused (a request with no `Origin` is accepted), 405 sessions or SSE not
registered, 400 missing session id, 404 session not acceptable (unknown, expired or another owner), 400 invalid `Last-Event-ID`,
409 a stream is already open for the session, 410 the id fell out of the retained window (resynchronise), 429 a stream limit is
reached. `DELETE`: 403, 400/404 from the gate, then 204 and the buffer is retired, which closes the attached stream.

**Host duties.** The host keeps `POST` (typically the ModelContextProtocol SDK): call `gate.AdmitPost`, and
`registry.Stream(issuedId)` for a newly issued session so messages published before the first GET are kept. Authenticate before
the endpoints: the principal is `HttpContext.User.Identity.Name`, and per-principal limits key on it. The `FoundgineMcpSseSweeper`
hosted service purges expired sessions and drops orphaned buffers every `SweepSeconds`.

**Single instance only.** The registry, buffers, limits and sweep are in-process. A multi-instance deployment needs a shared
event log and shared limits; until then use sticky routing or one instance.

UNVERIFIED: written without a .NET SDK; build and run `Foundgine.MCP.Tests` before relying on it.
