# SES-013 — Semantic Mutation Model and High-Assurance State Change

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Mutations require stronger guarantees than reads because execution changes state. The standard MUST treat mutation semantics as first-class.

## 2. Required mutation properties

Where applicable, a mutation contract MUST define:

- authorization scope;
- validation;
- preconditions;
- dependency ordering;
- generated values;
- atomicity;
- consistency;
- idempotency;
- replay behavior;
- concurrency behavior;
- compensation/recovery;
- audit/evidence.

## 3. Critique

The biggest missing area in many systems is **distributed mutation semantics**. Local database transactions do not solve multi-provider workflows, external side effects, or retries.

The future standard needs explicit profiles for:

- single-resource atomic mutation;
- transactional multi-step mutation;
- saga/compensation workflows;
- exactly-once-effect claims versus at-least-once delivery;
- idempotency keys;
- optimistic concurrency;
- pessimistic locking;
- external side effects.

## 4. Acceptance criteria

A mutation MUST have a defined outcome for validation failure, authorization failure, conflict, timeout, retry, duplicate request, partial provider failure, and recovery.

