# SES-017 — Execution Evidence, Errors, and Observability

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Evidence must make semantic execution explainable and auditable without becoming an authority channel or leaking protected data.

## 2. Required evidence

Evidence SHOULD cover:

- correlation/trace identity;
- semantic contract identity;
- operation graph identity;
- resolution outcome;
- authorization outcome;
- provenance verification;
- planner decisions;
- provider conformance result;
- execution outcome;
- mutation outcome;
- resource usage;
- error classification.

## 3. Critique

The future standard needs a stronger **privacy-preserving evidence model**. Logs can become a data-exfiltration channel if raw prompts, predicates, identifiers, secrets, or result payloads are recorded.

It also needs tamper evidence, retention policy, redaction, evidence schemas, and a clear distinction between operational telemetry and legally/audit-significant records.

## 4. Acceptance criteria

Evidence MUST be sufficient to reconstruct the security lineage of an execution while respecting declared data-classification and retention rules.

