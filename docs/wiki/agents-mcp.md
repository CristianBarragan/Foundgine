# AI agents, tool governance and MCP

## Purpose

Foundgine allows an agent or other tool caller to discover and invoke application capabilities through a controlled semantic boundary. The agent should express intent; it should not receive unrestricted database credentials or own application authorization rules.

## Tool discovery versus execution

Discovery describes available tools and their contracts. It does not grant execution authority. At invocation time, Foundgine must validate the request, ground semantic references, evaluate policy, validate the warrant and scope, and execute through a provider.

## MCP integration

The MCP adapter exposes Foundgine-backed tools through the Model Context Protocol. Tool schemas should be narrow, descriptive, and aligned with Foundgine semantic contracts. Avoid exposing raw SQL tools or broad administrative operations to general-purpose agents.

Read and mutation tools should be distinct. Mutations should use the controlled lifecycle: propose, dry-run, approve, execute. A caller must not be able to approve its own high-risk mutation merely because it can invoke the tool; approval policy belongs to the host's trusted governance boundary.

## Tool governance

The runtime control plane includes concepts such as tool descriptors/registry, policy gateway, risk rules, approval store, audit log, and a tool-call governor. These components help bound which tools may run, under which policy, at what risk level, and with what evidence.

## Agent integration patterns

- Keep the model/agent framework responsible for conversation and reasoning.
- Keep Foundgine responsible for semantic contracts, grounding, authorization, planning, and execution boundaries.
- Give agents the minimum tool surface needed for their task.
- Return concise semantic results and safe error categories.
- Require explicit human or policy approval for sensitive operations.
- Preserve correlation IDs and audit evidence across agent calls.

## Threat considerations

Prompt injection, tool-output injection, confused deputy behavior, tenant confusion, replay, over-broad tool schemas, and approval bypass are relevant threats. Treat agent-generated arguments as untrusted input even when they are valid JSON. Revalidate every operation at the trusted boundary.

## Where to look in source

- C#: `Foundgine.Providers/Tools/MCP`, `Foundgine.Runtime/ControlPlane`, agent models, and security/warrant code.
- Java: `foundgine-providers` MCP/tool integration, `foundgine-runtime` governance components, and the agent benchmark module.
- Existing guide: [AI agent integration](../AI-AGENT.md).
