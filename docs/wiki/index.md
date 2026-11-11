# Foundgine feature and capability wiki

This wiki explains Foundgine by **capability and architectural responsibility**, not by release. It is intended for application developers, platform engineers, provider authors, and contributors working across the .NET and Java implementations.

Foundgine is a semantic execution boundary: it lets applications, APIs, automation, and AI agents express intent against an application-owned semantic model without taking ownership of database access, authorization policy, or execution strategy.

## Start here

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

**Extensions and AI**

12. [Extensions](extensions) — GraphQL, Keycloak and Temporal in one place (the old Integrations page now points here).
13. [AI agents and MCP](agents-mcp) — safe tool discovery and invocation patterns.

**Trust, proof and operations**

14. [Insights, evidence and auditing](insights-evidence-auditing) — receipts, audit, diagnostics and live events.
15. [Approvals, audit and governance](approvals-audit-governance) — who said yes, and can we prove it.
16. [Conformance and hardening](conformance-hardening) — what is proven versus only implemented.
17. [AOT and generated metadata](aot) — build-time metadata generation and trimming-friendly deployment.
18. [Operations and extension points](operations) — configuration, diagnostics, testing, and adding capabilities.

**Reference**

19. [Cross-language implementation map](implementation-map) — where capabilities live in C# and Java.
20. [Glossary (plain words)](glossary) — short, friendly definitions.

> Looking for **EF Core** or **JPA**? Go to [ORM providers](orm-providers). Looking for GraphQL, Keycloak or Temporal? Go to [Extensions](extensions).

## Documentation principles

- **Feature first:** pages explain a capability, its purpose, its lifecycle, its constraints, and its components.
- **Version neutral:** version numbers belong in release notes and package metadata, not in the conceptual organization of the wiki.
- **Source-linked:** component names are tied to source folders and public API documentation. Exact signatures should always be checked against the code for the target package.
- **Honest parity:** a concept may exist in both implementations while provider coverage, hosting adapters, or operational maturity differ. See the implementation map and verify the concrete module before assuming parity.

## Canonical architectural invariant

> Metadata describes what exists. Semantic configuration describes what it means. Authorization describes what may be exercised. Intent describes what the caller wants.

Discovery and retrieval do not grant execution authority. A successful semantic match is not an authorization decision, and a plan is not permission to execute it.
