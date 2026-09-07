# SES-020 — Formal Semantic Execution Data Model

**Status:** Draft 1.0 — Normative target.

## 1. Purpose

This document defines the minimum typed artifact model required for independent SES implementations. A conforming implementation MAY use different in-memory types, but serialization, validation, identity, and security behavior MUST be equivalent.

## 2. Artifact graph

The canonical dependency chain is:

`Intent -> Contract -> SOG -> Resolution -> Authorization -> Provenance -> Plan -> IR -> Provider Gate -> Execution -> Evidence`.

No later artifact MAY silently reinterpret a security-relevant field established by an earlier artifact.

## 3. Required fields

Every normative artifact MUST have:

- `artifact_type`;
- `schema_version`;
- `artifact_id`;
- `created_at`;
- a canonical representation suitable for hashing;
- explicit references to upstream artifact identities where applicable.

Security-sensitive artifacts MUST additionally carry integrity/freshness information appropriate to their trust domain.

## 4. Semantic identity

Semantic identities MUST be stable under non-semantic serialization changes. They MUST change when any meaning-affecting property changes. Implementations MUST document identity scope and collision resistance.

## 5. Canonicalization

A canonical serializer MUST define ordering, Unicode normalization, numeric representation, null representation, duplicate handling, omitted/default values, escaping, and version markers. Hashing a non-canonical serialization is non-conformant.

## 6. Typed invariants

The following invariants are mandatory:

1. An Intent is not an authority artifact.
2. A resolution result is not an authorization decision.
3. A plan cannot widen the authorized SOG.
4. An IR cannot introduce semantics absent from its authorized plan.
5. A provider result cannot retroactively authorize execution.
6. Evidence cannot grant authority.

## 7. Formal relation

For an authorized execution:

`IR ⊑ Plan ⊑ Authorized(SOG, PolicySnapshot)`

where `⊑` means “contains no additional security- or meaning-relevant authority.” An implementation MUST define an executable checker for this relation.

## 8. Future development

Define a machine-readable schema, canonical binary/text encodings, schema evolution rules, typed extension registry, and independent cross-language test vectors.

## 9. Acceptance

Two independent implementations given the same canonical inputs MUST produce identical artifact identities and either equivalent artifacts or the same deterministic rejection class.
