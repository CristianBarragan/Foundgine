# SES-004 — Semantic Operation Graph

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

The Semantic Operation Graph (SOG) is the canonical representation of the complete operation before physical execution. It MUST be sufficient to reason about semantics, authorization, resource cost, and traversal topology without consulting provider-specific code.

## 2. Required information

The SOG MUST represent, as applicable:

- operation kind;
- semantic targets;
- selected fields;
- predicates;
- relationships/traversals;
- aggregates;
- ordering;
- pagination;
- parameters;
- mutation dependencies;
- expected cardinality;
- sensitivity;
- resource estimates;
- semantic contract identity/version.

## 3. Critique

The graph needs a formal treatment of **correlation, grouping, windowing, recursive traversal, subqueries, existential predicates, temporal joins, and result shaping**. Without these, “whole operation” is too narrow for serious analytical and agentic workloads.

The graph also needs explicit node identity and edge identity semantics so graph hashing cannot accidentally treat distinct security-relevant structures as equivalent.

## 4. Future requirements

Define canonical serialization, graph equivalence, graph hashing, graph diffing, graph normalization, recursive constructs, correlation scopes, and complexity accounting.

## 5. Acceptance criteria

A graph fingerprint MUST change for every security- or meaning-relevant mutation and remain stable across non-semantic serialization differences.

