# Foundgine.Providers.Models

`Foundgine.Providers.Models` integrates Foundgine with `Microsoft.Extensions.AI`.

## What is in this package

- `FoundgineAiToolset` — exposes Foundgine capabilities and queries as `AIFunction` tools.
- `FoundgineAiAgent` — runs a bounded Microsoft.Extensions.AI function-calling loop over an existing Foundgine runtime.

The package deliberately does not hard-code OpenAI, Azure, Ollama, or another model provider.

## Model-facing tools

The toolset exposes Foundgine operations such as:

```text
foundgine_capabilities
foundgine_query
```

The model supplies intent; Foundgine remains responsible for semantic resolution, authorization, planning, and provider execution.

## Security boundary

Capability discovery is not authorization. The host supplies security execution context; the model cannot choose or replace the caller's authority.

## Install

```bash
dotnet add package Foundgine.Providers.Models
```

Use this package when an AI/LLM application needs Foundgine semantic capabilities as controlled tool calls.
