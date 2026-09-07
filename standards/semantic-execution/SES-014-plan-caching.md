# SES-014 — Authorization-Safe Plan Caching

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

Caching MAY reuse planning work but MUST never become an implicit authorization grant.

## 2. Required cache dimensions

Cache identity MUST account for every semantic/security input that affects the plan, including contract version, graph shape, provider capabilities, relevant policy state, and plan version.

Request-specific values MUST be parameterized where safe rather than embedded into reusable artifacts.

## 3. Critique

The standard needs stronger treatment of **distributed cache coherence, revocation, policy freshness, multi-tenant isolation, cache poisoning, and cache stampede controls**.

A future implementation SHOULD distinguish:

- semantic plan cache;
- authorization decision cache;
- provider compilation cache;
- result cache.

These have different safety properties and MUST NOT be conflated.

## 4. Acceptance criteria

A cache hit MUST never bypass required freshness or execution-time authorization checks. Poisoning a cache entry MUST not broaden authority.

