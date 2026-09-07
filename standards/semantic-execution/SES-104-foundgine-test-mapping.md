# SES-104 — Future Implementation Gap Matrix
← [SES-103 — Security and Adversarial Conformance](SES-103-security-conformance.md) · [Standard Index](README.md) · [SES-105 — Portable Semantic Execution Conformance Vectors](SES-105-conformance-vectors.md) →

---

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Purpose

This document replaces “the current test tree proves the standard” with a forward-looking gap discipline. Existing implementation evidence is informative only.

## 2. Development priority

### P0 — security boundary

Must be complete before broad adoption:

- semantic identity and canonical serialization;
- whole-graph authorization;
- authorization provenance;
- execution IR verification;
- provider conformance gate;
- tenant isolation;
- fail-closed behavior;
- mutation replay/atomicity where mutations are supported.

### P1 — semantic determinism

- ambiguity/abstention;
- resolver evidence;
- algebraic equivalence;
- canonical graph hashing;
- provider semantic fidelity;
- generated/runtime parity.

### P2 — scale and operations

- adaptive resource budgets;
- distributed cache safety;
- asynchronous execution lineage;
- cancellation/backpressure;
- evidence privacy and retention.

### P3 — ecosystem

- portable vectors;
- formal provider profiles;
- agent authority/delegation;
- standard governance/version negotiation;
- certification tooling.

## 3. Critique rule

No feature should be marked “done” because the happy path works. It is done only when semantics, security, failure behavior, compatibility, and independent tests are specified.

## 4. Required engineering output

The repository SHOULD maintain a machine-readable gap file with every requirement and status. This document is the human-readable interpretation of that matrix.

The machine-readable gap registry is [`conformance/known-gaps.json`](conformance/known-gaps.json). It tracks the SES-019 roadmap backlog (`SES-RM-01` … `SES-RM-33`) and the documentation/registry audit findings below, each with an explicit `status`.

## 5. Documentation and registry audit findings (2026-09-07)

A repository audit found the following gaps in the standard's own supporting infrastructure. These are tracked as `SES-GAP-*` and `SES-PUB-*` entries in `conformance/known-gaps.json`.

| ID | Finding | Status |
|---|---|---|
| SES-GAP-01 | `conformance/requirements.json` had no root requirement for L0, L19–L26, or L101–L104 | Proven (fixed) |
| SES-GAP-02 | SES-100 defines test requirement IDs only for L0–L16; `scripts/verify-conformance.py` only checks that range. L17–L26 have no defined conformance tests | Missing |
| SES-GAP-03 | No machine-readable gap file existed despite §4 requiring one | Proven (fixed — see `conformance/known-gaps.json`) |
| SES-GAP-04 | The 34 SES-019 backlog items had no stable identifiers or status tracking | Proven (fixed — tracked as `SES-RM-01`…`SES-RM-33`) |
| SES-GAP-05 | SES-105 defines the portable vector format but no vector data files exist yet | Missing |
| SES-GAP-06 | SES-100 §8's `L#` section labels don't follow the `L#=SES-0##` convention used elsewhere; from L6 onward every section actually tests SES-0(n+2), and SES-002 (Canonical Lifecycle) / SES-007 (Logical Traversal) have no correctly-labeled tests at all — leaving two critical root requirements (`SES-LIFE-001`, `SES-TRV-001`) effectively untested | Missing |
| SES-PUB-01 | The standard isn't linked from `mkdocs.yml` nav, `docs/`, `docs-site/`, root `README.md`, or `llms.txt` | Missing |
| SES-PUB-02 | `mkdocs.yml` nav references three `docs/*.md` files that don't exist (`LAYER-SETUP.md`, `PRODUCT-IDENTITY.md`, `PROVIDER-INDEPENDENCE.md`) | Missing |
| SES-PUB-03 | Ten existing `docs/*.md` files are orphaned — not referenced in `mkdocs.yml` nav | Missing |
| SES-PUB-04 | No CI workflow invokes `verify-diagrams.py` or `verify-conformance.py` | Missing |

Remaining `Missing` items in this table are new backlog work and SHOULD be folded into the P0–P3 priorities in §2 during the next roadmap revision.

---

← [SES-103 — Security and Adversarial Conformance](SES-103-security-conformance.md) · [Standard Index](README.md) · [SES-105 — Portable Semantic Execution Conformance Vectors](SES-105-conformance-vectors.md) →
