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
approval records are rejected. Hosts still own key rotation, expiry and token-replay policy; these are not yet
implemented by the MCP adapter.
