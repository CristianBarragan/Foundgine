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
