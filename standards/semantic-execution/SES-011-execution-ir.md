# SES-011 — Execution Intermediate Representation
← [SES-010 — Semantic Planning, Normalization, and Safe Rewriting](SES-010-planning-and-rewrites.md) · [Standard Index](README.md) · [SES-012 — Provider Compilation, Conformance, and Execution Boundary](SES-012-provider-boundary.md) →

---

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Execution IR is the last provider-neutral representation before physical compilation. It MUST be explicit enough that a provider cannot reinterpret the semantic operation.

## 2. Required properties

The IR MUST carry or reference:

- semantic identity;
- authorized scope;
- provenance;
- parameter bindings;
- resource limits;
- required provider guarantees;
- result semantics;
- mutation guarantees where applicable.

## 3. Critique

The future standard needs a formal **IR type system** and validation model. Otherwise providers can receive structurally valid but semantically underspecified instructions.

It also needs explicit handling of secrets, opaque values, user-defined functions, nondeterminism, external calls, and side effects.

## 4. Future development

Define a versioned IR schema, verifier, canonical serialization, capability negotiation, type system, side-effect classification, and IR fuzzing suite.

## 5. Acceptance criteria

No provider may receive an IR artifact that has not passed semantic, authorization, resource, and provenance validation.

---

← [SES-010 — Semantic Planning, Normalization, and Safe Rewriting](SES-010-planning-and-rewrites.md) · [Standard Index](README.md) · [SES-012 — Provider Compilation, Conformance, and Execution Boundary](SES-012-provider-boundary.md) →
