# SES-021 — Lifecycle and Security State Machines
← [SES-020 — Formal Semantic Execution Data Model](SES-020-formal-data-model.md) · [Standard Index](README.md) · [SES-022 — Transport-Neutral Wire Protocol](SES-022-wire-protocol.md) →

---

**Status:** Draft 1.0 — Normative target.

## 1. Purpose

SES is not only a pipeline. Retries, reauthorization, cancellation, approvals, stale policy, provider failure, and partial mutation create state transitions. A conforming implementation MUST model these transitions explicitly.

## 2. Execution state machine

Required states:

`Received -> Resolved -> Authorized -> Planned -> Verified -> Executing -> Completed`

Terminal failure states MUST include at least `Rejected`, `Expired`, `Cancelled`, `ResourceExhausted`, `ProviderIncompatible`, and `Failed`.

## 3. Security transitions

- `Resolved -> Authorized` MUST occur only after whole-graph authorization.
- `Authorized -> Planned` MUST preserve authority.
- `Planned -> Verified` MUST verify provenance, freshness, IR integrity, and provider requirements.
- `Verified -> Executing` MUST NOT occur if any required proof is stale or invalid.
- Any security-context change MUST transition to a reauthorization state before execution continues.

## 4. Retry semantics

A retry MUST NOT implicitly recreate authority. Implementations MUST define whether the retry reuses, refreshes, or invalidates authorization provenance.

## 5. Mutation transitions

Mutations MUST additionally model `Prepared`, `Committed`, `Compensating`, and `Compensated` where the selected mutation profile permits partial failure.

## 6. Cancellation

Cancellation MUST be represented as a semantic event, not merely a transport disconnect. Providers MUST report whether cancellation was observed before, during, or after side effects.

## 7. Future development

Define a formal transition table, state/event/error registry, distributed execution lineage, human approval transitions, and recovery semantics.

## 8. Acceptance

Fault-injection tests MUST demonstrate that every reachable security-context change, timeout, cancellation, retry, and partial-failure path either preserves the stated invariant or terminates without unauthorized execution.

---

← [SES-020 — Formal Semantic Execution Data Model](SES-020-formal-data-model.md) · [Standard Index](README.md) · [SES-022 — Transport-Neutral Wire Protocol](SES-022-wire-protocol.md) →
