# Foundgine.Providers.Storage.Sql

`Foundgine.Providers.Storage.Sql` is Foundgine's SQL and PostgreSQL provider.

## What is in this package

### SQL query execution

- `SqlCompiler` — lowers `ExecutionIR`/logical plans to SQL.
- `SemanticQuerySqlWriter` — writes parameterized semantic queries.
- `SqlExecutionProvider` — executes compiled plans through ADO.NET.
- `SqlPlan` and `SqlParameterBinding` — physical SQL plan representation.
- `SqlCostEstimator` — provider cost estimates used as advisory planning input.

### SQL authorization and security

- `SqlAuthorizationWriter` — lowers provider-independent authorization predicates to SQL expressions and parameters.
- `SqlSecurityConformance` — checks that the compiled provider plan preserves required security invariants.

### PostgreSQL mutations

- `SqlMutationCompiler`
- `SqlMutationExecutionProvider`
- `SqlBatchedMutationPlan`
- `SqlBatchedMutationExecutionProvider`
- `PostgresBatchedMutationCompiler`
- `PostgresBatchedMutationExecutionProvider`
- mutation boundary/dependency support.

### PostgreSQL retrieval

`PostgresRetrievalCandidateSource` supplies candidate/evidence retrieval for lexical grounding:

| Strategy | Mechanism |
|---|---|
| `Relational` | structured PostgreSQL lookup |
| `Fuzzy` | `pg_trgm` |
| `FullText` | native PostgreSQL `tsvector` |
| `Search` | optional `pg_search` / BM25 |
| `GraphSimilarity` | optional Apache AGE |
| `Vector` | reserved for a separate pgvector provider |

Retrieval does not authorize a candidate; semantic resolution and authorization remain the Foundgine boundary.

### Pagination

`CursorCodec` and PostgreSQL correlation projection support cursor-based and provider-specific result handling.

## Install

```bash
dotnet add package Foundgine.Providers.Storage.Sql
```

Use this package when Foundgine should execute against SQL/PostgreSQL. It depends on the lower-level planning, metadata, and execution layers.
