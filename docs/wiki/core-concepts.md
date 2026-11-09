# Core concepts and vocabulary

## What Foundgine does

Foundgine sits between callers and application-owned data or operations. A caller may be a user-facing API, a background process, an automation, an MCP client, or an AI agent. Rather than allowing each caller to invent SQL, authorization checks, or mutation logic, Foundgine provides a shared semantic contract and controlled execution path.

It is not an ORM replacement, a database, an identity provider, a workflow engine, or an agent framework. It integrates with those systems and keeps their responsibilities explicit.

## The main nouns

| Concept | Meaning | Why it exists |
|---|---|---|
| Physical metadata | Tables, columns, keys, relationships, provider facts | Describes the underlying application structure |
| Semantic model | Application-defined meaning, entities, fields, relationships, aliases and policies | Gives callers stable business language |
| Semantic contract | A compiled, inspectable representation of the semantic model | Provides a boundary for resolution and execution |
| Intent | A caller's requested outcome, expressed as structured or natural-language-derived input | Separates what is wanted from how it is implemented |
| Grounding | Evidence-based resolution of words and references to semantic identities | Prevents ambiguous language from silently selecting data |
| Candidate retrieval | Finding possible semantic matches | Improves discovery without authorizing execution |
| Plan | A validated description of work to perform | Makes execution explicit and reviewable |
| Provider | An implementation that translates a plan into a concrete storage or service operation | Keeps physical execution replaceable |
| Warrant | A bounded, verifiable grant of authority for an operation and scope | Prevents semantic discovery from becoming implicit permission |
| Evidence | Results, audit records, decisions, and execution facts | Makes outcomes inspectable |

## The execution pipeline

```text
Caller
  -> Intent
  -> Semantic contract
  -> Candidate retrieval and grounding
  -> Authorization
  -> Semantic plan
  -> Execution representation
  -> Provider conformance gate
  -> Physical execution
  -> Result and evidence
```

Each stage has a distinct job. A caller cannot skip authorization by supplying a semantic identifier. A retrieval provider cannot grant access. A storage provider cannot broaden the warrant. An adapter should translate its protocol into Foundgine contracts rather than reimplementing the core semantics.

## Architectural boundaries

- **Core** owns stable contracts, semantic types, validation, resolution, planning abstractions, and security primitives.
- **Runtime** coordinates requests, plans, authorization, and execution lifecycle.
- **Providers** implement storage, transport, gateway, event, and tooling integrations.
- **Extensions** adapt external frameworks and infrastructure such as GraphQL, Keycloak, and Temporal.

The precise project/module layout is listed in [implementation map](implementation-map.md).

## Important non-equivalences

- A metadata entity is not automatically a semantic entity.
- An alias is not proof that a request is unambiguous.
- A candidate is not a selected target.
- A selected target is not an authorized target.
- A plan is not an approval.
- An approval is not necessarily an execution credential.
- A provider capability declaration is not proof that every request is safe for that provider.
- An event notification is not the source of truth for the underlying business record.

## When to use Foundgine

Use it when multiple callers need consistent, policy-controlled access to application capabilities; when AI or automation needs semantic discovery; when writes must be reviewed or approved; or when the application needs a stable facade over changing storage and integration details. For simple fixed CRUD endpoints, a conventional application service may be simpler.
