# SES-006 — Candidate Retrieval, Grounding, and Semantic Resolution
← [SES-005 — Semantic Operation, Predicate, and Traversal Algebra](SES-005-operation-algebra.md) · [Standard Index](README.md) · [SES-007 — Logical Traversal and Path Expansion](SES-007-logical-traversal.md) →

---

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Retrieval may propose candidates; resolution establishes meaning. No retrieval score, embedding similarity, lexical match, or model-generated suggestion may grant authority.

## 2. Required resolution model

The future standard MUST define deterministic behavior for:

- exact identity;
- aliases;
- synonyms;
- fuzzy retrieval;
- full-text retrieval;
- vector/embedding retrieval;
- BM25 or equivalent lexical retrieval;
- graph-based retrieval;
- multi-source candidate fusion;
- confidence thresholds;
- ambiguity;
- abstention.

## 3. Critique

This is one of the most important unresolved areas. “Semantic resolution” becomes dangerous if confidence is mistaken for truth. The standard needs a formal **ambiguity budget** and an explicit abstention protocol.

It also needs defenses against poisoned metadata, malicious descriptions, adversarial aliases, embedding manipulation, Unicode confusables, prompt injection in descriptions, and candidate-ranking instability.

## 4. Future development

Define a candidate evidence format, deterministic tie-breaking, provenance for every candidate, confidence calibration, ambiguity classes, multilingual normalization, and an independent resolution benchmark.

## 5. Acceptance criteria

The same contract, intent, and resolver configuration MUST yield a deterministic resolution result or deterministic abstention. Candidate retrieval alone MUST never be sufficient for authorization.

---

← [SES-005 — Semantic Operation, Predicate, and Traversal Algebra](SES-005-operation-algebra.md) · [Standard Index](README.md) · [SES-007 — Logical Traversal and Path Expansion](SES-007-logical-traversal.md) →
