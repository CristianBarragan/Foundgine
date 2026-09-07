# Semantic Execution Standard (SES) — Forward Specification

**Status:** Draft 1.0 — architecture, critique, and future development specification.

This specification defines what a serious Semantic Execution System should become. It is intentionally **not** a reverse-engineered description of Foundgine. Foundgine is an informative reference implementation and experimentation vehicle; it is not the normative source of the architecture.

## The target architecture

![SES canonical semantic execution lifecycle](diagrams/01-canonical-lifecycle.svg)

*Normative diagram source: [`diagrams/01-canonical-lifecycle.puml`](diagrams/01-canonical-lifecycle.puml). The SVG is the checked-in rendering artifact; PlantUML source is authoritative.*

The central design claim is that **meaning, authority, and execution are separate artifacts**. A caller can propose intent; only the trusted semantic contract establishes meaning; authorization establishes authority; planning determines how authorized meaning is executed; the provider conformance gate prevents physical execution from violating the semantic/security contract.

## What changed from the previous document set

The previous documents were primarily descriptive: they explained concepts by extracting them from an existing implementation. This revision is deliberately more demanding:

1. **Normative target first.** Each specification states what a conforming system must provide, not merely what the current code appears to do.
2. **Explicit gaps.** Every major layer has a critique section identifying capabilities that are easy to omit, under-specified, or likely to fail at scale.
3. **Future development is part of the specification.** Missing protocol definitions, proofs, registries, interoperability rules, governance, conformance suites, and operational controls are treated as first-class work.
4. **No implementation is assumed complete.** A test passing in Foundgine is evidence, not proof of standard conformance.
5. **Acceptance criteria are mandatory.** Future work should close a requirement only when the specified observable behavior and tests exist.

## Specification family

| ID | Document | Primary question |
|---|---|---|
| [SES-000](SES-000-terminology-and-conformance.md) | [Terminology and conformance](SES-000-terminology-and-conformance.md) | What exactly does conformance mean? |
| [SES-001](SES-001-architectural-model.md) | [Architecture and trust boundaries](SES-001-architectural-model.md) | What must the system separate? |
| [SES-002](SES-002-canonical-lifecycle.md) | [Canonical lifecycle](SES-002-canonical-lifecycle.md) | What is the required ordering? |
| [SES-003](SES-003-semantic-contract.md) | [Semantic contract](SES-003-semantic-contract.md) | What is the authoritative meaning of the domain? |
| [SES-004](SES-004-operation-graph.md) | [Operation graph](SES-004-operation-graph.md) | What is the canonical representation of an operation? |
| [SES-005](SES-005-operation-algebra.md) | [Operation algebra](SES-005-operation-algebra.md) | What transformations are valid? |
| [SES-006](SES-006-retrieval-and-resolution.md) | [Retrieval and resolution](SES-006-retrieval-and-resolution.md) | How does ambiguous language become deterministic meaning? |
| [SES-007](SES-007-logical-traversal.md) | [Traversal](SES-007-logical-traversal.md) | How are graph paths bounded and authorized? |
| [SES-008](SES-008-authorization-model.md) | [Authorization](SES-008-authorization-model.md) | What does it mean to authorize semantic meaning? |
| [SES-009](SES-009-authorization-provenance.md) | [Provenance](SES-009-authorization-provenance.md) | How is authorization bound to executable artifacts? |
| [SES-010](SES-010-planning-and-rewrites.md) | [Planning and rewrites](SES-010-planning-and-rewrites.md) | Which optimizations are safe and provable? |
| [SES-011](SES-011-execution-ir.md) | [Execution IR](SES-011-execution-ir.md) | What exact artifact may cross into execution? |
| [SES-012](SES-012-provider-boundary.md) | [Provider conformance](SES-012-provider-boundary.md) | How is physical execution proven faithful? |
| [SES-013](SES-013-mutations.md) | [Mutations](SES-013-mutations.md) | How are high-assurance state changes modeled? |
| [SES-014](SES-014-plan-caching.md) | [Plan caching](SES-014-plan-caching.md) | How can plans be reused without becoming stale authority? |
| [SES-015](SES-015-transport-and-agents.md) | [Transports and agents](SES-015-transport-and-agents.md) | How do MCP, GraphQL, APIs and agents converge safely? |
| [SES-016](SES-016-resource-governance.md) | [Resource governance](SES-016-resource-governance.md) | How is semantic complexity prevented from becoming DoS? |
| [SES-017](SES-017-evidence-and-observability.md) | [Evidence and observability](SES-017-evidence-and-observability.md) | What must be provable after execution? |
| [SES-018](SES-018-aot-and-generated-metadata.md) | [AOT and generated metadata](SES-018-aot-and-generated-metadata.md) | What can be compiled ahead without changing semantics? |
| [SES-019](SES-019-future-development-roadmap.md) | [Future development roadmap](SES-019-future-development-roadmap.md) | What remains to be developed before maturity? |
| [SES-020](SES-020-formal-data-model.md) | [Formal data model](SES-020-formal-data-model.md) | What are the typed normative artifacts and invariants? |
| [SES-021](SES-021-state-machines.md) | [State machines](SES-021-state-machines.md) | How are retries, cancellation, approval, and failure represented? |
| [SES-022](SES-022-wire-protocol.md) | [Wire protocol](SES-022-wire-protocol.md) | How do transports exchange semantic artifacts safely? |
| [SES-023](SES-023-versioning-and-compatibility.md) | [Versioning and compatibility](SES-023-versioning-and-compatibility.md) | How do independently evolving implementations interoperate? |
| [SES-024](SES-024-authority-and-delegation.md) | [Authority and delegation](SES-024-authority-and-delegation.md) | How is authority represented, attenuated, delegated, and revoked? |
| [SES-025](SES-025-provider-capability-profiles.md) | [Provider capability profiles](SES-025-provider-capability-profiles.md) | How is provider semantic fidelity declared and verified? |
| [SES-026](SES-026-proof-and-attestation.md) | [Proof and attestation](SES-026-proof-and-attestation.md) | How are semantic/security claims independently verified? |
| [SES-100](SES-100-conformance-testing.md) | [Conformance testing](SES-100-conformance-testing.md) | What must every layer prove? |
| [SES-101](SES-101-test-architecture-and-fixtures.md) | [Test architecture](SES-101-test-architecture-and-fixtures.md) | What fixtures and independent oracles are required? |
| [SES-102](SES-102-conformance-matrix.md) | [Conformance matrix](SES-102-conformance-matrix.md) | How are requirements traced to tests? |
| [SES-103](SES-103-security-conformance.md) | [Security conformance](SES-103-security-conformance.md) | What attacks must be resisted? |
| [SES-104](SES-104-foundgine-test-mapping.md) | [Future implementation gap matrix](SES-104-foundgine-test-mapping.md) | What should be built next? |
| [SES-105](SES-105-conformance-vectors.md) | [Portable vectors](SES-105-conformance-vectors.md) | How can implementations be compared? |
| [SES-900](SES-900-foundgine-reference-mapping.md) | [Reference implementation profile](SES-900-foundgine-reference-mapping.md) | How should Foundgine relate to the standard? |

Every document also carries a prev/next navigation line (with a link back to this index) directly under its title and again at its end, so the family can be read forward or backward in sequence without returning here each time.

## Formalization status

This revision now separates three things that must not be conflated: **architecture**, **normative requirements**, and **implementation evidence**. The next development step is therefore not another architecture diagram; it is executable formalization: machine-readable schemas, canonical encodings, state/event registries, compatibility negotiation, authority algebra, provider profiles, proof formats, and portable conformance vectors.

The machine-readable requirement registry is `conformance/requirements.json`. It is intentionally small at this stage: it defines the root requirement vocabulary and MUST be expanded until every normative paragraph has a stable identifier.

## Documentation and registry audit (2026-09-07)

A repository audit found several gaps in the standard's own supporting infrastructure — not in the pipeline architecture itself, but in how completely it is tracked, tested, and published:

- The root requirement registry, [`conformance/requirements.json`](conformance/requirements.json), had no entry for layers L0, L19–L26, or L101–L104. **Fixed** — all 34 layers (L0–L26, L100–L105, L900) now have a root requirement.
- [SES-100](SES-100-conformance-testing.md) only defines conformance test IDs for layers L0–L16, and its internal `L#` section labels don't consistently follow the `L#=SES-0##` convention used elsewhere — from L6 onward every section actually tests SES-0(n+2), and SES-002 (Canonical Lifecycle) and SES-007 (Logical Traversal) have no section testing their real subject matter at all. **Open** — see [SES-100 §8](SES-100-conformance-testing.md#8-layer-by-layer-mandatory-requirement-identifiers) and [SES-104 §5](SES-104-foundgine-test-mapping.md#5-documentation-and-registry-audit-findings-2026-09-07).
- [SES-104](SES-104-foundgine-test-mapping.md) called for a machine-readable gap file that didn't exist. **Fixed** — see [`conformance/known-gaps.json`](conformance/known-gaps.json), which also gives the 34-item [SES-019](SES-019-future-development-roadmap.md) backlog stable IDs (`SES-RM-01`…`SES-RM-33`) and tracked status.
- [SES-105](SES-105-conformance-vectors.md) defines a portable vector format but the repository has no vector data files yet. **Open.**
- The standard isn't linked from `mkdocs.yml` nav, the project's `docs/`/`docs-site/` trees, the root `README.md`, or `llms.txt`; `mkdocs.yml` nav also references three non-existent `docs/*.md` files and leaves ten existing ones unlinked; and no CI workflow runs `scripts/verify-diagrams.py` or `scripts/verify-conformance.py`. **Open** — all four items are outside this standards folder and are tracked in `conformance/known-gaps.json` under `SES-PUB-01`–`SES-PUB-04`.

The full findings, with status per item, are in [SES-104 §5](SES-104-foundgine-test-mapping.md#5-documentation-and-registry-audit-findings-2026-09-07) and [`conformance/known-gaps.json`](conformance/known-gaps.json).

## Global critique

A credible semantic execution architecture needs more than a good pipeline. The hard unsolved problems are **semantic determinism, proof-carrying authorization, provider fidelity, policy freshness, ambiguity management, resource economics, mutation correctness, cross-transport equivalence, interoperability, version negotiation, and independently verifiable conformance**.

A system should therefore be considered immature if it can demonstrate only happy-path queries. Production readiness requires adversarial, differential, concurrency, fault-injection, compatibility, and provider-fidelity evidence.

## Development principle

Every future feature should answer five questions before implementation:

1. What semantic contract does it add?
2. What new trust boundary does it introduce?
3. What invariant must never be violated?
4. What independent test can prove that invariant?
5. How does the feature interact with versioning, caching, authorization, providers, and resource limits?

## Diagrams

Every normative diagram MUST have a `.puml` source and a checked-in `.svg` artifact. PlantUML is the editable source of truth; SVG is the presentation artifact. `scripts/verify-diagrams.py` MUST fail on missing source/artifact pairs or broken Markdown references.

## Relationship to Foundgine

Foundgine is an **informative reference implementation/profile**. The standard must remain provider-neutral and must not require .NET, C#, PostgreSQL, SQL, GraphQL, MCP, EF Core, or any particular AI framework.

