# Foundgine feature and capability wiki

This wiki explains Foundgine by **capability and architectural responsibility**, not by release. It is intended for application developers, platform engineers, provider authors, and contributors working across the .NET and Java implementations.

Foundgine is a semantic execution boundary: it lets applications, APIs, automation, and AI agents express intent against an application-owned semantic model without taking ownership of database access, authorization policy, or execution strategy.

## Start here

1. [Core concepts](core-concepts.md) — the vocabulary and boundaries used throughout Foundgine.
2. [Semantic model and metadata](semantic-model.md) — how physical application metadata becomes a stable semantic contract.
3. [Intent, grounding and retrieval](intent-grounding.md) — how caller language is resolved without granting authority.
4. [Planning and execution](planning-execution.md) — how resolved requests become validated plans and provider work.
5. [Authorization and warrants](authorization-security.md) — how identity, scope, authority, and execution trust are separated.
6. [Mutation lifecycle](mutations.md) — propose, dry-run, approve, and execute.
7. [Gateway, events and live updates](gateway-events.md) — exposing capabilities through a stable facade.
8. [Providers and storage](providers-storage.md) — conceptual provider contracts and safety boundaries.
9. [Provider catalogue](providers.md) — detailed inventory, responsibilities, source locations and operational guidance for every provider family.
10. [AI agents and MCP](agents-mcp.md) — safe tool discovery and invocation patterns.
11. [AOT and generated metadata](aot.md) — build-time metadata generation and trimming-friendly deployment.
12. [Integrations](integrations.md) — overview of Keycloak, Temporal, GraphQL and host integration.
13. [Extension catalogue](extensions.md) — detailed feature, source and security guide for every framework extension.
14. [Operations and extension points](operations.md) — configuration, diagnostics, testing, and adding capabilities.
15. [Cross-language implementation map](implementation-map.md) — where capabilities live in C# and Java.

## Documentation principles

- **Feature first:** pages explain a capability, its purpose, its lifecycle, its constraints, and its components.
- **Version neutral:** version numbers belong in release notes and package metadata, not in the conceptual organization of the wiki.
- **Source-linked:** component names are tied to source folders and public API documentation. Exact signatures should always be checked against the code for the target package.
- **Honest parity:** a concept may exist in both implementations while provider coverage, hosting adapters, or operational maturity differ. See the implementation map and verify the concrete module before assuming parity.

## Canonical architectural invariant

> Metadata describes what exists. Semantic configuration describes what it means. Authorization describes what may be exercised. Intent describes what the caller wants.

Discovery and retrieval do not grant execution authority. A successful semantic match is not an authorization decision, and a plan is not permission to execute it.
