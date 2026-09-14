# Foundgine.Providers.Storage.PostgresVector

`Foundgine.Providers.Storage.PostgresVector` is the optional PostgreSQL `pgvector` provider for Foundgine lexical grounding.

## What is in this package

- `PgVectorOptions` — configuration for the pgvector integration.
- `PgVectorSemanticLexiconIndexClient` — stores/indexes embeddings for a projected frozen semantic lexicon.
- `PgVectorSemanticLexicalCandidateSource` — retrieves nearest-neighbor lexical candidates from PostgreSQL/pgvector.
- `AssemblyInfo` package metadata.

## Boundary

```text
frozen semantic contract
        ↓
semantic lexicon projection + embeddings
        ↓
PostgreSQL / pgvector
        ↓
ranked candidate + evidence
        ↓
SemanticLexicalResolver
        ↓
authorization / planning / execution
```

Vector similarity is a retrieval signal, not semantic authority. The frozen contract remains authoritative for topology and legal semantic paths.

This package is separate from `Foundgine.Providers.Storage.Sql`: SQL execution and PostgreSQL relational retrieval live in `Foundgine.Providers.Storage.Sql`, while this package supplies pgvector-backed approximate retrieval.

## Install

```bash
dotnet add package Foundgine.Providers.Storage.PostgresVector
```

Use it when vector similarity is useful for grounding free-form language against a Foundgine semantic lexicon.
