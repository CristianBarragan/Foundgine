# AI Agents with Foundgine

Foundgine gives AI agents a controlled application capability surface without giving the model database authority.

## The boundary

![Foundgine AI agent capability boundary](assets/agent-tool-boundary.svg)

The agent proposes structured intent. Foundgine resolves, validates, authorizes, plans and executes it through the same boundary used by other callers.

## Capability discovery is advisory

Discovery describes what the application exposes. It does not grant permission. The server evaluates authorization again for every actual operation.

## Host-owned security

Authentication, actor identity, tenant context, secrets and model orchestration remain host responsibilities.

## Calling from TypeScript, Python or WebAssembly

Thin client SDKs (`@foundgine/typescript`, `foundgine-sdk`, `@foundgine/wasm`) connect to a Foundgine server's MCP endpoint and Temporal workflow routes. They are transport only and grant no authority; see the [client SDK overview](https://github.com/Foundgine/Foundgine/blob/main/docs/wiki/client-sdks.md).

## Go deeper

Read the repository [AI agent guide](https://github.com/Foundgine/Foundgine/blob/main/docs/AI-AGENT.md).

## Continue

[Security](../security/index.html) → [Samples](../samples/index.html)
