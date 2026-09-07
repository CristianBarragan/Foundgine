# SES-010 — Semantic Planning, Normalization, and Safe Rewriting

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Planning converts authorized semantics into an executable strategy without changing meaning or authority.

## 2. Required planner properties

A planner SHOULD support:

- canonical normalization;
- cost-based alternatives;
- provider capability awareness;
- join/traversal strategy selection;
- predicate pushdown;
- projection pruning;
- pagination planning;
- aggregate planning;
- resource estimation;
- rewrite certificates.

## 3. Critique

The missing piece is **proof-carrying optimization**. “The rewrite is equivalent” is not enough when authorization predicates, null semantics, row multiplicity, or provider behavior can change.

The future standard MUST classify rewrites as:

1. semantics-preserving and security-preserving;
2. semantics-preserving but requiring security revalidation;
3. semantics-changing and therefore requiring a new authorization decision;
4. prohibited.

## 4. Future development

Define a rewrite certificate format, reference optimizer, cost-model contract, optimizer equivalence oracle, and adversarial rewrite corpus.

## 5. Acceptance criteria

Optimized and reference plans MUST produce equivalent authorized results for all applicable conformance vectors, and any failed proof MUST stop execution rather than silently fall back.

