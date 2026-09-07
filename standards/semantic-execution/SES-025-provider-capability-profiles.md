# SES-025 — Provider Capability and Fidelity Profiles

**Status:** Draft 1.0 — Normative target.

## 1. Purpose

Provider conformance is semantic fidelity, not successful execution. A provider MUST declare what it can preserve and what it cannot.

## 2. Capability declaration

A provider profile MUST describe support for:

- semantic types and operators;
- null and duplicate semantics;
- collation/locale behavior;
- ordering guarantees;
- pagination behavior;
- aggregation/windowing;
- traversal;
- transaction and isolation guarantees;
- concurrency/conflict semantics;
- cancellation;
- resource limits;
- authorization predicate fidelity;
- consistency and visibility guarantees;
- mutation/idempotency guarantees.

## 3. Fidelity classes

Each capability MUST be classified as `Exact`, `EquivalentUnderConditions`, `BestEffort`, or `Unsupported`. `BestEffort` MUST NOT be used for a security-critical semantic property without explicit authorization.

## 4. Provider conformance gate

The gate MUST compare required plan properties against the provider profile. If the provider cannot guarantee a required property, the system MUST reject, choose an explicitly authorized alternative, or downgrade only under an explicit policy.

## 5. Differential testing

Provider profiles MUST be backed by portable vectors. At least one alternate provider or independent reference evaluator SHOULD be used for materially important semantics.

## 6. Future development

Define a capability schema, profile registry, fidelity proof format, provider attestation, transaction profiles, and portable differential harness.

## 7. Acceptance

A provider claiming exact fidelity MUST pass every applicable semantic vector. A deliberately incompatible provider MUST be rejected before physical execution when a required property is unsupported.
