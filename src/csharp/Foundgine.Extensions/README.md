# Foundgine.Extensions

## Purpose

`Foundgine.Extensions` contains optional framework integrations around the Foundgine semantic execution boundary. The current v2 package primarily provides the Hot Chocolate GraphQL integration.

## What this package provides

- Hot Chocolate GraphQL schema/translation integration for Foundgine semantic operations.
- GraphQL-facing adapters that translate supported GraphQL operations into Foundgine intent and runtime concepts.
- Integration points for using Foundgine authorization/planning/execution from a GraphQL application instead of implementing a second independent execution path.

## What it does not provide

This package does not provide:

- The Foundgine runtime itself.
- SQL/PostgreSQL, in-memory, Elasticsearch, or vector storage providers.
- An MCP server/transport.
- An LLM client.

## What is expected from the consumer

The consuming application must configure Hot Chocolate, register `Foundgine.Runtime`, provide a semantic model and authorization policy, and install/configure the appropriate concrete provider from `Foundgine.Providers`. GraphQL remains an application transport; Foundgine remains the semantic execution boundary.

## Install

```bash
dotnet add package Foundgine.Extensions --version 2.0.3
```

## Typical use

Use this package when your application exposes GraphQL and you want GraphQL requests to participate in Foundgine semantic resolution, authorization, planning and execution. If you do not use GraphQL/Hot Chocolate, you generally do not need this package.
