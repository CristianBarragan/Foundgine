# SES-001 — Architectural Model and Trust Boundaries

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Target architecture

The system MUST preserve this conceptual separation:

`Caller → Intent → Semantic Contract → Semantic Operation Graph → Candidate Retrieval/Resolution → Authorization → Semantic Plan → Execution IR → Provider Conformance Gate → Physical Execution → Evidence`

The stages MAY be physically fused, but their contracts and trust properties MUST remain observable and testable.

## 2. Critical invariants

- Untrusted input MUST NOT create authority.
- Structural metadata MUST NOT silently become semantic authority.
- Resolution MUST NOT grant permission.
- Authorization MUST cover the complete semantic operation.
- Planning MUST NOT widen authorized scope.
- Execution IR MUST NOT reinterpret semantic meaning.
- Providers MUST NOT silently discard security constraints.
- Evidence MUST NOT become authority merely because it exists.

## 3. Critique — what is still under-specified

### 3.1 Boundary ownership
The standard needs a precise ownership model for every field crossing every boundary. “Trusted context” is too broad. Future versions SHOULD define field-level provenance classes such as caller-controlled, host-derived, contract-derived, policy-derived, planner-derived, provider-derived, and evidence-only.

### 3.2 Asynchronous and distributed execution
The lifecycle is currently easiest to reason about in-process. A production standard MUST specify what happens when planning and execution are separated by queues, services, retries, workflow engines, or multiple trust domains.

### 3.3 Human approval
High-risk operations need an approval boundary. The standard SHOULD define approval as a constrained authorization input with identity, scope, expiry, purpose, and non-repudiable binding—not as a Boolean flag attached to an operation.

### 3.4 Multi-party authority
Delegation, service identities, impersonation, acting-on-behalf-of, and capability attenuation are not sufficiently captured by a single principal model. These need a formal authority algebra.

## 4. Future requirements

A complete architecture specification MUST define distributed execution lineage, authority delegation, approval semantics, trust-domain transitions, and failure behavior for partially completed workflows.

## 5. Acceptance criteria

The architecture is mature only when a test can take an artifact, mutate each trust-classified field independently, and demonstrate that no lower-trust mutation can cross a higher-trust boundary without explicit reauthorization.

