# SES-900 — Foundgine Reference Implementation Profile (Informative)

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Purpose

Foundgine SHOULD be documented as a reference implementation profile, not as the source from which the standard is reverse engineered.

## 2. Relationship to the standard

The standard defines the target. Foundgine demonstrates selected design ideas and provides a concrete experimentation environment.

The mapping SHOULD therefore be maintained as:

`SES requirement → Foundgine implementation candidate → Foundgine test evidence → known deviation`

rather than:

`Foundgine behavior → presumed standard requirement`.

## 3. Required disclosure

The profile MUST identify features that are:

- fully demonstrated;
- partially demonstrated;
- experimental;
- not implemented;
- provider-specific;
- intentionally outside the standard.

## 4. Strategic benefit

This separation allows Foundgine to evolve rapidly without forcing every experiment into the normative specification. It also makes it possible for other implementations to satisfy SES using different languages, providers, policy engines, and execution technologies.

