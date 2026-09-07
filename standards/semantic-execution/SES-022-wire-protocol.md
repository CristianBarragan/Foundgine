# SES-022 — Transport-Neutral Wire Protocol
← [SES-021 — Lifecycle and Security State Machines](SES-021-state-machines.md) · [Standard Index](README.md) · [SES-023 — Versioning, Negotiation, and Compatibility](SES-023-versioning-and-compatibility.md) →

---

**Status:** Draft 1.0 — Normative target.

## 1. Purpose

Different transports MUST converge on one semantic execution contract. MCP, GraphQL, REST, RPC, SDKs, and agent protocols MAY expose different syntax but MUST NOT define independent authorization semantics.

## 2. Envelope

A future normative envelope MUST contain:

- protocol/version;
- request and correlation identifiers;
- caller/session identity reference;
- semantic contract reference;
- intent payload;
- declared resource budget;
- delegation/approval references where applicable;
- integrity and replay metadata;
- response/error envelope.

## 3. Trust rules

Transport-provided claims MUST be classified as untrusted until validated by the host trust domain. Tool descriptions, schema descriptions, labels, natural-language metadata, and agent-generated arguments MUST NOT directly grant capability.

## 4. Canonical errors

The protocol MUST distinguish at least:

`InvalidInput`, `UnknownSemantic`, `AmbiguousSemantic`, `Unauthorized`, `StaleAuthorization`, `InvalidProvenance`, `UnsupportedProvider`, `ResourceExceeded`, `Conflict`, `Cancelled`, and `ExecutionFailure`.

An implementation MUST NOT collapse security rejection into a successful transport response.

## 5. Equivalence

Equivalent semantic requests sent over two conformant transports MUST resolve to equivalent SOGs and authorization decisions, subject only to explicitly documented transport context differences.

## 6. Version negotiation

Peers MUST negotiate protocol, semantic-contract, operation-graph, provenance, IR, and capability versions before execution when compatibility is not implicit.

## 7. Future development

Specify canonical JSON/CBOR encodings, capability discovery, signed envelopes, replay protection, delegation tokens, streaming semantics, pagination, and backward/forward compatibility rules.

## 8. Acceptance

A transport-conformance suite MUST replay the same semantic vector through at least two transport adapters and compare canonical semantic, authorization, provenance, and error outcomes.

---

← [SES-021 — Lifecycle and Security State Machines](SES-021-state-machines.md) · [Standard Index](README.md) · [SES-023 — Versioning, Negotiation, and Compatibility](SES-023-versioning-and-compatibility.md) →
