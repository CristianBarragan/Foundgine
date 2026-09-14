# Foundgine.Providers.Storage.Elasticsearch

`Foundgine.Providers.Storage.Elasticsearch` is an optional Elasticsearch/OpenSearch integration for Foundgine lexical grounding.

## What is in this package

- `SemanticLexiconIndexClient` — projects a frozen Foundgine semantic contract into a search index.
- `ElasticsearchSemanticLexicalCandidateSource` — retrieves ranked lexical candidates for semantic grounding.

The projection can represent semantic kinds such as entities, fields, relationships, traversals, values, and operations. Domain values can be indexed separately when they are expected to be resolved from live data.

## Boundary

Elasticsearch provides retrieval evidence and ranked hypotheses:

```text
semantic contract
      ↓
semantic lexicon projection
      ↓
Elasticsearch
      ↓
candidate + evidence
      ↓
Foundgine.Core.Semantic resolver
      ↓
authorization
```

A search score is never authorization and cannot redefine the semantic graph.

## Install

```bash
dotnet add package Foundgine.Providers.Storage.Elasticsearch
```

Use this package when free-form language needs Elasticsearch/OpenSearch lexical retrieval before semantic resolution.
