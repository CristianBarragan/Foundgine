# SES-008 — Semantic Authorization Model
← [SES-007 — Logical Traversal and Path Expansion](SES-007-logical-traversal.md) · [Standard Index](README.md) · [SES-009 — Authorization Provenance and Security Proof](SES-009-authorization-provenance.md) →

---

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Authorization MUST answer whether a trusted principal/context may exercise the fully resolved semantic operation.

## 2. Required authority dimensions

The standard SHOULD support:

- principal identity;
- tenant/resource scope;
- capability/action;
- ownership;
- purpose;
- data classification;
- environment/context;
- temporal validity;
- delegation;
- approval;
- quotas and risk limits.

## 3. Critique

A simple allow/deny model is inadequate for agentic systems. Future authorization needs **obligations, conditions, attenuation, delegation, step-up approval, purpose limitation, and dynamic policy state**.

The standard also needs a formal distinction between **authentication**, **authorization**, **delegation**, and **execution-time revalidation**.

## 4. Future development

Define an authority algebra, policy evaluation contract, policy decision provenance, delegation model, obligations, revocation semantics, and policy freshness requirements.

## 5. Acceptance criteria

A conforming implementation MUST be able to demonstrate that changing any caller-controlled authority claim does not change trusted authorization unless the host explicitly accepts and validates that claim.

---

← [SES-007 — Logical Traversal and Path Expansion](SES-007-logical-traversal.md) · [Standard Index](README.md) · [SES-009 — Authorization Provenance and Security Proof](SES-009-authorization-provenance.md) →
