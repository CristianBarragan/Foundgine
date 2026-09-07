# SES-003 — Semantic Contract and Domain Meaning

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Objective

The Semantic Contract is the authoritative description of what domain concepts mean and which operations are available. It MUST be distinct from raw storage schema.

## 2. Required contract content

A mature contract SHOULD define:

- semantic identities;
- entity and value types;
- fields and cardinality;
- relationships and directionality;
- aliases and synonyms;
- operation capabilities;
- nullability and absence semantics;
- units and currencies where relevant;
- temporal semantics;
- sensitivity/classification;
- mutability;
- constraints;
- semantic versions;
- deprecation rules;
- authorization-relevant attributes.

## 3. Critique — major missing semantic dimensions

### 3.1 Temporal semantics
“Current”, “as of”, effective dates, event time, processing time, and historical visibility require a formal model.

### 3.2 Units and normalization
A semantic field representing money, distance, quantity, or rates needs canonical units and conversion rules. Silent unit conversion can change meaning.

### 3.3 Locale and linguistic semantics
Synonyms, morphology, Unicode normalization, locale, transliteration, and multilingual labels need deterministic treatment.

### 3.4 Data classification
Sensitivity and export restrictions should be semantic properties, not ad-hoc provider behavior.

### 3.5 Lifecycle/deprecation
The contract needs explicit evolution semantics so an alias cannot accidentally resolve to a retired or security-sensitive concept.

## 4. Future development

Define a versioned semantic contract schema, canonical serialization, compatibility rules, temporal/measurement semantics, classification vocabulary, and contract migration protocol.

## 5. Acceptance criteria

Two independent implementations given the same contract snapshot MUST derive identical semantic identities and compatible operation capabilities.

