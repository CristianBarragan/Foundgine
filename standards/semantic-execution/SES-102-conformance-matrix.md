# SES-102 — Requirement Traceability and Conformance Matrix

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

The matrix is a living engineering artifact, not documentation theater.

## 2. Required states

Every requirement MUST be one of:

- **Missing** — no implementation or test evidence;
- **Partial** — some behavior exists but one or more normative conditions are absent;
- **Implemented / unproven** — implementation exists but independent evidence is insufficient;
- **Proven** — implementation and required tests pass;
- **Not applicable** — profile explicitly excludes the requirement.

## 3. Critique

The previous mapping approach risks treating names of test classes as evidence. That is not strong enough. The future matrix MUST identify exact test methods and expected invariants.

## 4. Required machine-readable fields

`requirement_id`, `profile`, `layer`, `severity`, `implementation_reference`, `test_reference`, `fixture`, `oracle`, `status`, `provider`, `security_invariant`, `last_verified`, `known_gap`.

## 5. Acceptance criteria

CI MUST fail for any claimed profile containing a normative requirement with status Missing, unless an explicitly approved conformance exception exists.



## 6. Machine-readable registry

The normative registry is `conformance/requirements.json`. It is the seed registry, not a complete claim of conformance. The registry MUST grow until every normative requirement has a stable identifier and traceability record.

## 7. Required CI behavior

For every claimed profile, CI MUST reject a release when an applicable critical requirement is Missing or Implemented / unproven. Exceptions MUST be explicit, versioned, approved, and visible in the conformance manifest.

## 8. Independence rule

A test that merely exercises the same implementation logic used to produce the expected result is not an independent oracle. At least one independent oracle class MUST be used for security-critical semantic, authorization, rewrite, and provider-fidelity requirements.
