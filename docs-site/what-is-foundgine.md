# What Is Foundgine?

Foundgine is a programmable semantic execution platform for .NET. It separates **caller intent**, **application meaning**, **authorization**, and **physical execution**.

## The problem

Applications often expose the same business capabilities through APIs, GraphQL, JSON, MCP and AI agents. Without a shared boundary, each surface can duplicate validation, authorization and data access.

## The Foundgine model

![Foundgine semantic execution boundary](assets/what-is-foundgine-plantuml-01.svg)

Callers describe what they want. The application defines what exists and what is allowed. Foundgine resolves the request and carries the authorized meaning into provider execution.

## Why it matters for AI

An agent can propose structured intent without becoming the authority over database schema, tenant identity, credentials or business invariants. Capability discovery is advisory; every real operation is resolved and authorized again.

## What it is not

Foundgine is not an ORM replacement, database, identity provider, authorization server, model provider or general autonomous-agent framework.

## Ambiguity is explicit

Retrieval is evidence, not truth. When free-form language has two genuinely different legal meanings, the semantic resolver can require clarification instead of silently executing the highest-scoring candidate. See [Grounding decisions](https://github.com/Foundgine/Foundgine/blob/main/docs/GROUNDING-DECISIONS.md).

## Continue

[Get started](getting-started/index.html) → [Architecture](architecture/index.html)
