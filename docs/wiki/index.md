# Foundgine feature and capability wiki

This wiki explains Foundgine by **capability and architectural responsibility**, not by release. It is intended for application developers, platform engineers, provider authors, and contributors working across the .NET and Java implementations.

Foundgine is a configurable semantic and execution boundary for application capabilities. It separates application meaning, persistence mapping, authorization and execution from the interfaces and agents that consume them. Fluent configuration is the primary developer experience; conventional application use does not require AI, MCP or an agent framework. It complements existing application infrastructure and supports incremental adoption.

## Start here

**Evaluating Foundgine?** Begin with [Why Foundgine?](why-foundgine.md) for the enterprise problem, where it fits alongside existing technology, the expected business value, and what should be measured in a pilot.


> **New to this? In plain words.** Foundgine sits between your callers (apps, APIs, AI agents) and your data. **Providers** do the database and infrastructure work; **extensions** connect tools like GraphQL, Keycloak and Temporal. Foundgine decides what is allowed.

**Concepts**

1. [Core concepts](core-concepts) — the vocabulary and boundaries used throughout Foundgine.
2. [Semantic model and metadata](semantic-model) — how physical application metadata becomes a stable semantic contract.
3. [Intent, grounding and retrieval](intent-grounding) — how caller language is resolved without granting authority.
4. [Planning and execution](planning-execution) — how resolved requests become validated plans and provider work.
5. [Authorization and warrants](authorization-security) — how identity, scope, authority, and execution trust are separated.
6. [Mutation lifecycle](mutations) — propose, dry-run, approve, and execute.
7. [Gateway, events and live updates](gateway-events) — exposing capabilities through a stable facade.

**Providers (databases, ORMs, search, events)**

8. [Provider catalogue](providers) — **every provider by name**, in plain words: SQL, **EF Core**, **JPA**, in-memory, Elasticsearch, pgvector, PostgreSQL retrieval, event store, outbox, gateway, MCP, AI models.
9. [ORM providers: EF Core (.NET) and JPA (Java)](orm-providers) — use the ORM you already have; permissions are still always enforced; honest maturity table.
10. [Providers and storage](providers-storage) — conceptual provider contracts and safety boundaries.
11. [Fluent semantic reads (`SemanticQuery`)](semantic-query-builder) — build a read with the same builder in C# and Java; generic CRUD next to it.

**Extensions, AI and client SDKs**

12. [Extensions](extensions) — GraphQL, Keycloak and Temporal in one place (the old Integrations page now points here).
13. [AI agents and MCP](agents-mcp) — safe tool discovery and invocation patterns.
14. [Agent and framework integrations: MAF, LangChain4j, Spring Boot](agent-framework-integrations) — put Foundgine behind agent frameworks and host it in Spring.
15. [Client SDKs: TypeScript, Python, WebAssembly](client-sdks) — thin clients for calling a running server's MCP and workflow endpoints.

**Trust, proof and operations**

16. [Insights, evidence and auditing](insights-evidence-auditing) — receipts, audit, diagnostics and live events.
17. [Approvals, audit and governance](approvals-audit-governance) — who said yes, and can we prove it.
18. [Conformance and hardening](conformance-hardening) — what is proven versus only implemented.
19. [AOT and generated metadata](aot) — build-time metadata generation and trimming-friendly deployment.
20. [Operations and extension points](operations) — configuration, diagnostics, testing, and adding capabilities.

**Reference**

21. [Cross-language implementation map](implementation-map) — where capabilities live in C# and Java.
22. [Glossary (plain words)](glossary) — short, friendly definitions.

> Looking for **EF Core** or **JPA**? Go to [ORM providers](orm-providers). Looking for GraphQL, Keycloak or Temporal? Go to [Extensions](extensions). Using Microsoft Agent Framework, LangChain4j or Spring Boot? Go to [Agent and framework integrations](agent-framework-integrations). Calling a server from TypeScript, Python or WebAssembly? Go to [Client SDKs](client-sdks).

## Documentation principles

- **Feature first:** pages explain a capability, its purpose, its lifecycle, its constraints, and its components.
- **Version neutral:** version numbers belong in release notes and package metadata, not in the conceptual organization of the wiki.
- **Source-linked:** component names are tied to source folders and public API documentation. Exact signatures should always be checked against the code for the target package.
- **Honest parity:** a concept may exist in both implementations while provider coverage, hosting adapters, or operational maturity differ. See the implementation map and verify the concrete module before assuming parity.

## Canonical architectural invariant

> Metadata describes what exists. Semantic configuration describes what it means. Authorization describes what may be exercised. Intent describes what the caller wants.

Discovery and retrieval do not grant execution authority. A successful semantic match is not an authorization decision, and a plan is not permission to execute it.
