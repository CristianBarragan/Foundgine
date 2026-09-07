# SES-000 — Terminology, Normative Language, and Conformance

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

The standard currently risks becoming a collection of architectural opinions unless it defines an objective conformance model. This document establishes the minimum vocabulary, artifact model, versioning rules, and evidence required to call an implementation conformant.

## 2. Required normative objects

A conforming implementation MUST expose or internally preserve distinguishable representations for at least:

- Intent
- Semantic Contract
- Semantic Operation Graph (SOG)
- Resolution Result
- Authorization Decision
- Authorization Provenance
- Semantic Plan
- Execution IR
- Provider Conformance Result
- Execution Result
- Evidence

Equivalent internal representations are allowed, but a security boundary MUST remain independently testable.

## 3. Conformance levels

The future standard SHOULD define independently certifiable profiles:

| Profile | Minimum scope |
|---|---|
| Core | semantics, graph, algebra, resolution |
| Secure | Core + authorization + provenance + execution gate |
| Provider | Secure + provider fidelity and capability negotiation |
| Mutation | Provider + atomicity/idempotency/replay/fault guarantees |
| Agent | Mutation + transport/tool/agent equivalence |
| Static/AOT | Secure/Provider requirements + generated metadata parity |
| Full | all mandatory profiles |

An implementation MUST declare the profiles it claims. “Conformant” without a profile is insufficient.

## 4. Major gap: semantic versioning of the standard

Future work MUST define compatibility rules for:

- semantic contract versions;
- operation-graph versions;
- policy versions;
- provenance versions;
- provider capability versions;
- vector/schema versions;
- standard revisions.

A consumer MUST be able to reject an incompatible artifact deterministically.

## 5. Major gap: machine-readable conformance manifest

A future conformance manifest SHOULD identify every requirement, implementation feature, test, provider profile, and known exception. The manifest MUST distinguish **implemented**, **tested**, **proven**, and **not implemented**. A code file or passing unit test is not itself conformance evidence.

## 6. Acceptance criteria

Conformance tooling is complete only when it can answer, mechanically:

- which profile is claimed;
- which normative requirements apply;
- which test vectors prove each requirement;
- which provider capabilities are required;
- which requirements remain unproven.

## 7. Requirement identifier scheme

Normative requirements MUST use stable identifiers. The recommended namespace is `SES-<DOMAIN>-<NNN>`, where DOMAIN identifies the normative area and NNN is monotonically assigned. Requirement identifiers MUST NOT be silently reused after deletion; withdrawn requirements remain reserved.

Examples include `SES-ARCH-001`, `SES-SEM-001`, `SES-SOG-001`, `SES-AUTH-001`, `SES-IR-001`, and `SES-PROVIDER-001`. Cross-layer requirements use `SES-X-<NNN>`.

## 8. Conformance evidence

A requirement is **Proven** only when the implementation reference, independent test, fixture/vector, oracle, expected invariant, and verification result are all recorded. Passing an end-to-end test without requirement-level attribution is insufficient.

## 9. Future work

Develop the full requirement registry, machine-readable conformance manifest, compatibility matrix, certification rules, version-negotiation protocol, normative schemas, state/event registry, provider profile registry, and portable proof formats. The registry MUST eventually be sufficient for an implementation team with no access to Foundgine to build and test an interoperable implementation.

