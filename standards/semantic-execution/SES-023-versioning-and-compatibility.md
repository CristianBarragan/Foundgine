# SES-023 — Versioning, Negotiation, and Compatibility

**Status:** Draft 1.0 — Normative target.

## 1. Version domains

SES MUST treat these as independently versioned domains:

- standard specification;
- semantic contract;
- SOG schema;
- operation algebra;
- policy/authority model;
- provenance schema;
- Execution IR;
- provider capability profile;
- transport envelope;
- conformance vectors.

## 2. Compatibility

A version MUST declare whether it is backward compatible, forward compatible, conditionally compatible, or incompatible. “Same major version” alone is insufficient evidence.

## 3. Negotiation

Before execution, peers MUST establish a mutually supported capability set. Unsupported semantics MUST result in deterministic rejection or an explicitly authorized fallback that preserves required invariants.

## 4. Security rule

A downgrade MUST NOT remove a security requirement silently. Security profiles, authorization semantics, provenance requirements, and mutation guarantees MUST be monotonic unless an explicit policy permits the weaker profile.

## 5. Cache interaction

Cache keys MUST include every version domain whose change can alter meaning, authority, provider behavior, or resource safety.

## 6. Generated artifacts

Generated metadata and IR MUST record the source schema/contract version and generator version. Stale generated artifacts MUST be rejected or regenerated deterministically.

## 7. Future development

Define a formal compatibility algebra, negotiation transcript, downgrade rules, migration tooling, and compatibility test vectors.

## 8. Acceptance

A compatibility harness MUST prove that supported combinations execute, unsupported combinations fail closed, and security-relevant downgrade attempts cannot broaden authority.
