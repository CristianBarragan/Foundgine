# Foundgine.Core.Serialization

`Foundgine.Core.Serialization` is the JSON transport adapter for Foundgine structured read intent.

## What is in this package

- `JsonReadIntentAdapter` — converts JSON input into provider-independent Foundgine read intent.
- `JsonReadIntentAdapterOptions` — controls adapter behavior and resource/shape limits.

The adapter works at the intent boundary. It does not know SQL syntax or execute a database operation.

## Boundary

```text
JSON payload
    ↓
Foundgine.Core.Serialization
    ↓
ReadIntent
    ↓
Foundgine.Core.Semantic
```

Semantic validation and authorization remain downstream responsibilities.

## Install

```bash
dotnet add package Foundgine.Core.Serialization
```

Use this package when an HTTP/JSON or other JSON-speaking caller needs to submit dynamic, provider-neutral Foundgine intent.
