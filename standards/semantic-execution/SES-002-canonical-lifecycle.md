# SES-002 — Canonical Semantic Execution Lifecycle

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Required lifecycle

Every conforming execution MUST have an equivalent logical ordering:

1. accept intent;
2. establish trusted semantic context;
3. construct or validate the Semantic Operation Graph;
4. retrieve candidate meanings;
5. resolve meaning deterministically or reject ambiguity;
6. authorize the resolved operation;
7. create a semantic plan;
8. bind authorization provenance;
9. compile Execution IR;
10. pass the provider conformance gate;
11. execute;
12. emit evidence.

## 2. Critique

The lifecycle needs stronger treatment of **loops and re-entry**. Real systems reauthorize after policy changes, retry failed providers, refresh credentials, or re-resolve stale references. A linear diagram is insufficient for those cases.

The future specification MUST model:

- reauthorization loops;
- stale semantic contracts;
- provider retry boundaries;
- partial mutation recovery;
- cancellation;
- timeout;
- human approval insertion;
- asynchronous execution;
- compensation workflows.

## 3. Rejection semantics

Each stage MUST define explicit rejection codes and MUST identify whether retrying the same artifact is safe. Security failures SHOULD NOT be conflated with transient infrastructure failures.

## 4. Acceptance criteria

A conforming lifecycle implementation MUST produce a traceable lineage from the original intent to physical execution, including every revalidation and every rejection/retry decision.

