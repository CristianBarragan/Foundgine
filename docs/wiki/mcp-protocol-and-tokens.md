# MCP protocol versions, sessions and approval tokens

This page records the concrete MCP behaviour that is implemented in source (version 3.3.5). For the conceptual overview see [AI agents and MCP](agents-mcp.md).

## Protocol versions

| Component | Behaviour |
| --- | --- |
| TypeScript, Python and WASM SDKs | Request protocol version `2025-06-18` by default (`DEFAULT_PROTOCOL_VERSION`); it can be overridden in the client options. |
| C# and Java agent clients | Send the header `MCP-Protocol-Version: 2025-06-18`. |
| Java MCP server (`FoundgineMcpServer`) | Latest is `2025-06-18`; it also accepts `2025-03-26` and `2024-11-05`. |
| C# MCP server | Built on the ModelContextProtocol C# SDK; supported versions are whatever that SDK negotiates. |

## Sessions and server-to-client streams

- Sessions use an opaque `Mcp-Session-Id` header. In C#, `FoundgineMcpSessionManager` issues ids from 256 random bits (base64url), applies a sliding idle timeout, an absolute lifetime and a session cap, and stores only a digest.
- Unknown, expired or foreign-owner sessions all return the same HTTP 404.
- With sessions on, a client can open a long-lived `GET` for server-initiated messages and reconnect with `Last-Event-ID`. A reconnect whose id has left the retained window gets `410` and must resynchronise.
- The SSE registry, buffers and limits are in-process: use one instance or sticky routing until a shared event log exists.

## Approval tokens (`fgmcp1`)

The MCP approve/execute tools use one signed-token format shared by C# and Java.

- Wire form: `fgmcp1.BASE64URL(payload).BASE64URL(signature)`.
- Signature: RSA PKCS#1 v1.5 with SHA-256 over the exact UTF-8 payload bytes. RSA keys must be at least 2048 bits.
- Payload: a newline-delimited UTF-8 record of base64url text fields (key id, approval id, plan fingerprint, approved-by, approved-at in Unix milliseconds, request JSON), so no serializer-specific JSON canonicalisation is needed.
- Execution verifies the signature before parsing the mutation JSON, binds the current host security context, and revalidates the exact plan fingerprint. Unsigned legacy approval records are rejected.
- Expiry and replay are enforced by `FoundgineMcpApprovalTokenPolicy`. The host owns the replay store and private-key custody.
- Key rotation: `FoundgineMcpApprovalKeyRing` signs with one active key and keeps older public keys verifiable for the retention window. Key ids are never reused; `Revoke` is immediate.
- The golden interoperability vector is `src/conformance/mcp-approval-token-golden.json`; do not regenerate its key or token casually.

!!! note
    The C# MCP README still marks its session and SSE implementation `UNVERIFIED` (written without a .NET SDK). Build and run `Foundgine.MCP.Tests` before relying on it.
