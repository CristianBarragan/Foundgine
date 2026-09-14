# Foundgine.Providers.Storage.InMemory

`Foundgine.Providers.Storage.InMemory` is a deterministic, non-SQL Foundgine execution provider.

## What is in this package

- `InMemoryDataSet` — an in-memory collection of provider rows.
- `InMemoryRow` — CLR-backed row/value representation.
- `InMemoryPlan` — physical representation of the supported in-memory execution subset.
- `InMemoryCompiler` — lowers Foundgine execution IR/plans into in-memory operations and performs provider security-conformance checks.
- `InMemoryExecutionProvider` — the `IExecutionProvider` integration.

## Purpose

Use this provider for:

- fast deterministic tests;
- examples and local development;
- proving provider independence;
- planner/execution tests that should not require PostgreSQL.

It is intentionally a limited provider rather than a replacement for a production database.

## Boundary

```text
Semantic operation → Planning → ExecutionIR → InMemory → CLR-backed rows
```

The same logical execution boundary can instead be lowered by `Foundgine.Providers.Storage.Sql`.

## Install

```bash
dotnet add package Foundgine.Providers.Storage.InMemory
```
