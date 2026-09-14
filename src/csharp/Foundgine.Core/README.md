# Foundgine.Core

## Purpose

`Foundgine.Core` is the foundation of Foundgine v2. It contains the provider-independent contracts and semantic structures that describe **what an application or agent wants to do**, without coupling that intent to a database, GraphQL server, MCP transport, LLM, or specific execution provider.

## What this package provides

- Semantic model and semantic entities/relationships.
- Metadata and structural discovery primitives.
- Read and mutation intent contracts and serialization support.
- Provider-independent intermediate representations, execution contracts, and planning primitives.
- Core abstractions, identifiers, contracts, predicates, projections, traversal and operation structures used by Foundgine.

## What it does not provide

This package does **not** execute database operations and does not provide:

- PostgreSQL, SQL, in-memory, Elasticsearch, or vector storage providers.
- GraphQL hosting or Hot Chocolate integration.
- MCP transport or an LLM client.
- Application orchestration or concrete runtime execution. `Foundgine.Core.Execution` contains contracts/IR only; `Foundgine.Runtime` performs orchestration.

## When to install it

Install `Foundgine.Core` when you are building or consuming the semantic layer directly, implementing a custom provider/integration, or need the shared Foundgine contracts without the higher-level runtime and provider stack. Most ordinary applications should also install `Foundgine.Runtime` and an appropriate `Foundgine.Providers` integration.

## What is expected from the consumer

You are responsible for creating/composing the semantic model and supplying the execution/runtime components needed by your application. `Foundgine.Core` deliberately does not choose a storage system or application transport for you.

## Install

```bash
dotnet add package Foundgine.Core --version 2.0.3
```

## Relationship to other v2 packages

`Foundgine.Core` is the base layer. `Foundgine.Runtime` builds the application execution boundary on top of it; `Foundgine.Extensions` provides optional integrations; `Foundgine.Providers` supplies concrete providers and integrations.
