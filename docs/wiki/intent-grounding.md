# Intent, grounding and retrieval

## Purpose

Intent describes the outcome a caller wants. Grounding resolves the caller's terms to known semantic identities using explicit evidence. Retrieval finds possible candidates. These stages make the system usable without allowing vague language to silently become executable authority.

## Intent forms

Foundgine supports structured semantic requests and adapters that compile supported input formats into a common intent representation. The exact accepted syntax is package/API specific; use the Open Intent API guide and the public API reference for current signatures.

A read intent typically expresses a target, selected fields, filters, ordering, pagination, and optional relationship traversal. Mutation intent expresses a requested change and its effects/dependencies. Natural-language interpretation may happen before Foundgine; Foundgine's job is to validate and ground the resulting semantic request rather than trust arbitrary model output.

## Grounding stages

1. Normalize the request and enforce input budgets.
2. Extract entity, field, relationship, and value references.
3. Generate candidate semantic identities from the compiled contract and configured candidate sources.
4. Score candidates using permitted evidence such as exact identity, lexical evidence, aliases, and optional embedding-backed retrieval.
5. Apply evidence gates, ambiguity thresholds, candidate limits, and time/path budgets.
6. Return a resolved target, a clarification requirement, or a safe failure.
7. Only after grounding, continue to authorization and planning.

## Lexical evidence and alias weights

Aliases improve discoverability. They must not be treated as equivalent to model-owned identity. Stronger lexical evidence can rank candidates, but a weight is not an authorization score. The evidence gate determines which kinds of evidence may justify a resolution. An ambiguous request should ask for clarification or fail closed rather than choose the first candidate.

## Candidate retrieval strategies

Candidate sources may be contract-backed, approximate, vector-backed, or external search indexes. A retrieval strategy selects the appropriate source and limits its work. A retrieval timeout or truncation is a bounded result, not permission to accept a weak candidate.

The pgvector and Elasticsearch integrations are retrieval/index providers; they do not replace the semantic contract and do not authorize the returned candidate.

## Clarification and decision records

A grounding decision should be inspectable: what was requested, what candidates were considered, which evidence applied, whether the result was ambiguous or truncated, and why the system resolved or declined. Do not record sensitive raw values unnecessarily; apply the host application's data retention policy.

## Security boundary

**Discovery is not authority.** A candidate result may reveal that a semantic concept exists, but the caller still needs authorization for the requested operation and scope. Do not expose hidden fields or entities merely because a search index contains them; filter retrieval according to the application's discovery policy where required.

## Where to look in source

- C#: `Semantic/Intent`, `Semantic/Resolution`, `Semantic/Resolution/Lexical`, `Semantic/Resolution/Retrieval*`, `SemanticRequestResolver.cs`.
- Java: `foundgine-core/.../semantic/intent`, `resolution`, and `resolution/lexical` packages.
- Existing guides: [Open Intent API](../OPEN-INTENT-API), [Lexical grounding](../LEXICAL-GROUNDING), [Grounding decisions](../GROUNDING-DECISIONS).
