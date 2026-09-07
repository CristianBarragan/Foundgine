# SES-105 — Portable Semantic Execution Conformance Vectors

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Portable vectors allow different implementations and providers to be compared without sharing internal code.

## 2. Vector model

Each vector SHOULD contain:

`id`, `standard_version`, `profile`, `contract`, `intent`, `trusted_context`, `expected_graph`, `expected_authorization`, `expected_provenance`, `expected_plan_properties`, `expected_result_or_error`, `resource_budget`.

## 3. Mandatory future vector families

At minimum:

- authorized read;
- unknown capability;
- ambiguous resolution;
- cross-tenant read;
- cross-tenant mutation;
- traversal escape;
- predicate removal;
- stale provenance;
- cache poisoning;
- prompt injection;
- graph explosion;
- provider capability mismatch;
- mutation atomicity failure;
- mutation replay;
- concurrency conflict;
- optimized/reference equivalence;
- transport equivalence;
- generated/runtime parity;
- temporal semantics;
- null/duplicate/order semantics.

## 4. Critique

Vectors need explicit expected **negative behavior**, not just expected results. An implementation that returns an error for the wrong reason may still be insecure.

## 5. Acceptance criteria

A vector is complete only when it identifies the invariant being tested and distinguishes authorization failure, semantic ambiguity, resource rejection, provider incompatibility, and operational failure.

