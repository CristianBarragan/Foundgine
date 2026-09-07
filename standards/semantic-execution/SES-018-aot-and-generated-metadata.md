# SES-018 — AOT, Generated Metadata, and Deterministic Compilation

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Ahead-of-time generation can move stable semantic knowledge from runtime into compile-time artifacts, improving startup, memory, determinism, and deployment characteristics. It MUST NOT create a second semantic truth.

## 2. Required properties

Generated artifacts MUST have:

- deterministic identity;
- source-contract fingerprint;
- generator version;
- schema/version compatibility;
- reproducible output;
- runtime parity checks;
- invalidation rules.

## 3. Critique

The difficult problem is not code generation itself; it is **semantic drift between generated and live metadata**. The future standard must specify what happens when source models, policies, providers, or generated artifacts are out of sync.

Generated metadata MUST also be prevented from inferring authorization solely from storage shape.

## 4. Future development

Define a generated-artifact manifest, reproducible-build profile, source-to-artifact dependency graph, stale-artifact detection, generator compatibility protocol, and runtime/generated differential suite.

## 5. Acceptance criteria

Generated and runtime semantic models MUST produce identical identities and equivalent operation graphs for the same source contract, or execution MUST be rejected.

