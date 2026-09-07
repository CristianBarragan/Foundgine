# SES-005 — Semantic Operation, Predicate, and Traversal Algebra
← [SES-004 — Semantic Operation Graph](SES-004-operation-graph.md) · [Standard Index](README.md) · [SES-006 — Candidate Retrieval, Grounding, and Semantic Resolution](SES-006-retrieval-and-resolution.md) →

---

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

The algebra defines what transformations preserve meaning. It is the foundation for optimization, caching, equivalence testing, and proof.

## 2. Required algebra

The future standard MUST formalize:

- Boolean predicate algebra;
- comparison/null semantics;
- projection;
- selection;
- ordering;
- pagination;
- grouping/aggregation;
- join/traversal composition;
- existential/universal predicates;
- mutation dependency composition;
- result-shaping operators.

## 3. Critique

The largest gap is that algebraic equivalence is easy to state and hard to prove under **nulls, duplicates, ordering, floating-point behavior, collation, temporal predicates, provider-specific three-valued logic, and nondeterministic functions**.

A future standard MUST classify operations as deterministic, context-dependent, volatile, or side-effecting and MUST restrict rewrites accordingly.

## 4. Future development

Create a reference algebra, canonical normal forms, rewrite certificates, counterexample generation, and an independent equivalence oracle.

## 5. Acceptance criteria

Every optimizer rewrite MUST identify its algebraic preconditions and have differential tests covering cases where those preconditions fail.

---

← [SES-004 — Semantic Operation Graph](SES-004-operation-graph.md) · [Standard Index](README.md) · [SES-006 — Candidate Retrieval, Grounding, and Semantic Resolution](SES-006-retrieval-and-resolution.md) →
