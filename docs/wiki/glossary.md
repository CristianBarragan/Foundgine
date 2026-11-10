# Glossary (plain words)

| Term | Plain-words meaning |
|---|---|
| **Alias** | A nickname for an entity or field ("client" for Customer). Helps search, grants nothing. |
| **Approval** | A recorded "yes, this exact plan may run", tied to the plan's fingerprint, the requester and an expiry. |
| **Approval store (atomic)** | Storage that saves the approval and its audit/outbox record in one transaction, so you never get one without the other. |
| **AOT (ahead-of-time)** | Compiling so the app needs no runtime guessing (reflection). Foundgine generates metadata at build time to support it. |
| **Audit log** | The record of who asked what and what was decided. A durable one only counts once storage has confirmed it. |
| **Authorization** | The decision "may this caller do this, here?" |
| **Candidate** | A possible match for a word. Not yet chosen, not yet allowed. |
| **Capability** | Something Foundgine can do (e.g. grounding), or something a provider declares it can do. Declared is not proven. |
| **Cardinality** | One-to-one, one-to-many or many-to-many between entities. |
| **Certified** | Proven by an actual run of tests/CI. Distinct from "implemented". |
| **Clarification** | Foundgine asking the caller which of several matches they meant. |
| **Conformance** | Tests proving different implementations behave identically (C# vs Java, provider vs provider). |
| **Connection** | How a model member reaches a physical storage entity, with conversions and authorization. |
| **Contract (semantic)** | The compiled, frozen model with a fingerprint. |
| **Cursor** | An opaque bookmark for "next page", bound to the query and scope that made it. |
| **Dry-run** | Check a write and preview effects without committing. Not a lock. |
| **Dimension** | A named attribute of an entity in the model. |
| **Entity** | A business thing callers can address (Customer, Order). |
| **Event store** | Durable history of events. |
| **Evidence** | Receipts, decisions and fingerprints that let you reconstruct what happened. |
| **Execution IR** | The plan in provider-neutral form, handed to a provider compiler. |
| **Fail closed** | When unsure or misconfigured, deny; never fall back to allow. |
| **Fingerprint** | A deterministic seal identifying exact content (a model, a plan, a warrant). |
| **Gateway** | A transport-neutral front door other products call; delegates to the runtime. |
| **Grounding** | Matching words to real model items using evidence. |
| **High assurance** | Opt-in runtime capability for stricter warrant handling (dev default uses ephemeral keys; use real keys in production). |
| **Idempotent** | Doing it twice has the same effect as once; needed because delivery and retries can repeat. |
| **Intent** | What the caller wants, in provider-neutral form. |
| **JPA** | Jakarta Persistence, Java's ORM API. Foundgine has a JPA provider (partial, unverified). |
| **EF Core** | .NET ORM. Foundgine's .NET ORM provider targets it. |
| **MCP** | Model Context Protocol, how AI agents discover and call tools. |
| **Metadata** | Structural facts about what exists. Not permission. |
| **Mutation** | A write modelled as a reviewable semantic operation. |
| **Nonce** | A one-time value that stops a warrant being reused (replay protection). |
| **Outbox** | A durable to-do list of messages written with the data change and sent later (at least once). |
| **Plan** | A validated description of work. |
| **Plan cache** | Reuse of structural compilation only. Never caches an allow decision. (Design item; see hardening.) |
| **Principal** | The caller's identity. |
| **Provider** | The adapter that executes plans against a real system or supplies discovery evidence. |
| **Replay** | Re-sending a valid old request or warrant. Defended by nonces and replay stores. |
| **Retrieval** | Finding candidates (lexical, fuzzy, vector...). Never authorizes. |
| **Saga / compensation** | A multi-step process that can undo earlier steps if a later one fails (Temporal). |
| **Scope** | The slice of data/tenant an authority covers. |
| **Security execution context** | Trusted identity, tenant and warrant passed into execution. |
| **Semantic model** | Your business dictionary. |
| **Tenant** | One customer/organisation whose data must stay separate. |
| **Tenant-first gate** | Checks warrant and tenant before any schema/name lookup. |
| **Tool-call governor** | Control-plane component that risk-scores, policy-checks, approves and audits agent tool calls. |
| **Warrant** | A signed, short-lived, scoped permission slip, verified at execution. |
| **Workflow (Temporal)** | A long-running, durable business process. |
| **ORM** | A tool that maps your classes to database tables so you do not write SQL by hand. |
| **Extension** | An optional plug-in that connects a tool (GraphQL, Keycloak, Temporal) to Foundgine. It translates; it never decides permissions. |
| **Receipt** | A fingerprint-only record that an execution happened. See [Insights, evidence and auditing](insights-evidence-auditing.md). |
| **Diagnostics** | Metrics and traces showing how the system behaves; hints, not proof. |
