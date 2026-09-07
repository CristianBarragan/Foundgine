# SES-101 — Test Architecture, Fixtures, and Independent Oracles
← [SES-100 — Forward Conformance Testing Specification](SES-100-conformance-testing.md) · [Standard Index](README.md) · [SES-102 — Requirement Traceability and Conformance Matrix](SES-102-conformance-matrix.md) →

---

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Tests MUST be capable of detecting a broken implementation even when the same bug exists in the implementation's helper utilities.

## 2. Required independent oracles

Use independent expected graphs, reference fixtures, a deliberately simple reference planner, alternate providers, algebraic properties, canonical serialization, and explicit security invariants.

## 3. Canonical fixture

The standard SHOULD maintain a portable fixture containing multiple tenants, ownership, aliases, ambiguous terms, relationships of different cardinalities, nullable fields, protected data, mutations, aggregates, recursive paths, and provider capability differences.

## 4. Critique

The current testing concept needs stronger **metamorphic testing**. Future tests should generate a request and then apply transformations that should or should not preserve results, authority, cost bounds, or provenance.

## 5. Fault model

The suite MUST inject:

- provider timeout;
- partial write;
- policy revocation;
- stale contract;
- stale cache;
- corrupted provenance;
- altered IR;
- malformed transport payload;
- concurrency conflict;
- cancellation.

## 6. Acceptance criteria

Every security-critical test MUST state why its oracle is independent of the code path being tested.

---

← [SES-100 — Forward Conformance Testing Specification](SES-100-conformance-testing.md) · [Standard Index](README.md) · [SES-102 — Requirement Traceability and Conformance Matrix](SES-102-conformance-matrix.md) →
