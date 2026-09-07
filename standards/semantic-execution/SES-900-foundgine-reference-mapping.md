# SES-900 — Foundgine Reference Implementation Profile (Informative)
← [SES-105 — Portable Semantic Execution Conformance Vectors](SES-105-conformance-vectors.md) · [Standard Index](README.md) · *(end of family)*

---

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

## 5. Reference implementation matrix (2026-09-07 audit)

Built from `src/` and `tests/` in the Foundgine repository, following the §2 schema (`SES requirement → Foundgine implementation candidate → Foundgine test evidence → status`). The full machine-readable version, including a `deviation` field per row, is [`conformance/foundgine-mapping.json`](conformance/foundgine-mapping.json). Status values follow §3: fully demonstrated, partially demonstrated, experimental, not implemented, provider-specific, not applicable.

| SES | Topic | Implementation candidate | Test evidence | Status |
|---|---|---|---|---|
| [SES-000](SES-000-terminology-and-conformance.md) | Terminology and conformance | `FoundgineOptions` / `FoundgineCapability` (feature flags only) | — | Not implemented |
| [SES-001](SES-001-architectural-model.md) | Trust boundaries | `SecurityExecutionContext`, `ISecurityExecutionContextProvider` | `AdversarialAgentBoundaryTests`, Security.Tests/Penetration | Partially demonstrated |
| [SES-002](SES-002-canonical-lifecycle.md) | Canonical lifecycle | `FoundgineEngine`, `IFoundgine` | `AuthorizationGoldenPathTests`, `ArchitectureBoundaryTests` | Partially demonstrated |
| [SES-003](SES-003-semantic-contract.md) | Semantic contract | `ISemanticContractProvider`, `SemanticContractSnapshot`, `SemanticContractAttestation` | `SemanticContractProviderTests`, `SemanticContractAttestationTests`, `SemanticContractRuntimeBoundaryTests` | Fully demonstrated |
| [SES-004](SES-004-operation-graph.md) | Operation graph | `SemanticOperationGraph`, `SemanticOperationGraphSafetyValidator` | `SemanticOperationGraphStep30Tests`, `SemanticOperationGraphSafetyStep32Tests` | Fully demonstrated |
| [SES-005](SES-005-operation-algebra.md) | Operation algebra | `SemanticOperationAlgebra` | `SemanticAlgebraTests`, `ExecutionAlgebraInvariantTests` | Fully demonstrated |
| [SES-006](SES-006-retrieval-and-resolution.md) | Retrieval and resolution | `EntityResolver`, `SemanticLexicalResolver`, pgvector/Elasticsearch candidate sources | `SemanticRequestResolverTests`, `SemanticApproximateRetrievalTests`, Postgres.Vector.Tests | Fully demonstrated |
| [SES-007](SES-007-logical-traversal.md) | Logical traversal | `SemanticTraversal` | `OpenIntentTraversalTests`, `NestedCollectionTraversalTests` | Partially demonstrated |
| [SES-008](SES-008-authorization-model.md) | Authorization model | `SemanticAuthorizer`, `ISemanticAuthorizationPolicy` | `SemanticAuthorizationTests`, `AuthorizationGoldenPathTests`, `AuthorizationInvariantTests` | Fully demonstrated |
| [SES-009](SES-009-authorization-provenance.md) | Authorization provenance | `SemanticPlanAuthorizationBinding`, `SecurityWarrant*` | `AuthorizationPreservationProofTests`, Security.Authority.Tests provenance/integrity suite | Fully demonstrated |
| [SES-010](SES-010-planning-and-rewrites.md) | Planning and rewrites | `Planner`, `IPlanRewriteRule`, `RewriteRuleComposer` | `PlannerTests`, `PlanOptimizationProofTests`, `RewriteRuleCompositionTests` | Fully demonstrated |
| [SES-011](SES-011-execution-ir.md) | Execution IR | `ExecutionIR`, `ExecutionIRBoundary` | `ExecutionIRTests`, `ExecutionBoundaryTests` | Fully demonstrated |
| [SES-012](SES-012-provider-boundary.md) | Provider boundary | `IExecutionProvider`, `SqlExecutionProvider`, `InMemoryProvider` | `ProviderExecutableConformanceGateTests`, `ProviderSecurityConformanceMatrixTests` | Provider-specific (SQL/Postgres + in-memory only) |
| [SES-013](SES-013-mutations.md) | Mutations | `SemanticMutationBuilder`, `MutationPlanner`, `PostgresBatchedMutationExecutionProvider` | `ComplexSemanticMutationE2ETests`, `TransferFundsTests`, Postgres transfer-funds suite | Fully demonstrated |
| [SES-014](SES-014-plan-caching.md) | Plan caching | `IProviderPlanCache`, `MemoryProviderPlanCache` | `PlanCacheTests`, `ContextSafePlanCacheTests`, `SecurityPlanCachePartitionTests` | Partially demonstrated (in-memory only) |
| [SES-015](SES-015-transport-and-agents.md) | Transports and agents | `FoundgineMcpTools`, `HotChocolateSemanticAdapter` | MCP.Tests (8 files), GraphQL.HotChocolate.Tests (26 files), `MultiProducerEquivalenceTests` | Provider-specific (MCP + GraphQL only) |
| [SES-016](SES-016-resource-governance.md) | Resource governance | `SecurityResourceLimits`, `MutationSecurityResourceLimitValidator` | `SecurityResourceLimitTests`, `ResourceExhaustionPenetrationTests` | Partially demonstrated |
| [SES-017](SES-017-evidence-and-observability.md) | Evidence and observability | `ExecutionEvidence`, `ExecutionReceipt`, `IAuditLog` | `EvidenceTests`, `ExecutionReceiptUnificationTests` | Partially demonstrated |
| [SES-018](SES-018-aot-and-generated-metadata.md) | AOT and generated metadata | `GeneratedSemanticField`, `FoundgineMetadataGenerator` | Foundgine.Aot.Tests (all 3 files) | Fully demonstrated |
| [SES-019](SES-019-future-development-roadmap.md) | Future development roadmap | — (backlog document) | — | Not applicable |
| [SES-020](SES-020-formal-data-model.md) | Formal data model | — (C# POCOs only, no schema artifact) | — | Not implemented |
| [SES-021](SES-021-state-machines.md) | State machines | `SecurityWarrantDelegationStateMachine`, `SecurityWarrantTrustTransition` | `SecurityWarrantDelegationStateMachineSecurityTests`, `SecurityWarrantTrustTransitionSecurityTests` | Partially demonstrated (security-warrant only; no lifecycle-level state machine) |
| [SES-022](SES-022-wire-protocol.md) | Wire protocol | `JsonReadIntentAdapter` (transport-specific only) | `JsonReadIntentAdapterTests` | Not implemented |
| [SES-023](SES-023-versioning-and-compatibility.md) | Versioning and compatibility | `SemanticVersion`, `SemanticModelFingerprint` | `SemanticModelFingerprintTests` | Experimental |
| [SES-024](SES-024-authority-and-delegation.md) | Authority and delegation | `SecurityWarrantDelegationChain`, `SecurityWarrantDelegationCompromise/Concurrency` | Warrant delegation chain/compromise/concurrency security tests, `WarrantTrustBoundaryPenetrationTests` | Fully demonstrated |
| [SES-025](SES-025-provider-capability-profiles.md) | Provider capability profiles | `SemanticCapabilityContract`, `AggregateProviderCapability` | `AggregateProviderCapabilityTests`, `ProviderSecurityConformanceMatrixTests` | Partially demonstrated (SQL/Postgres only) |
| [SES-026](SES-026-proof-and-attestation.md) | Proof and attestation | `AggregateRewriteProof`, `AuthorizationPreservationProof`, `SecurityInvariantProof`, `SemanticContractAttestation` | Matching `*ProofTests`/`*AttestationTests` across Semantics.Tests and Planning.Tests | Fully demonstrated |
| [SES-100](SES-100-conformance-testing.md) | Conformance testing | `scripts/verify-conformance.py` | 246 xUnit tests across 13 projects (informative only) | Partially demonstrated |
| [SES-101](SES-101-test-architecture-and-fixtures.md) | Test architecture, fixtures, oracles | — | — (no 'oracle'/'differential' concept anywhere in the codebase) | Not implemented |
| [SES-102](SES-102-conformance-matrix.md) | Conformance matrix | `conformance/requirements.json`, `conformance/known-gaps.json` | — | Partially demonstrated (seed registry only, not full traceability) |
| [SES-103](SES-103-security-conformance.md) | Security conformance | — | Security.Tests/Penetration (11 files), Security.Authority.Tests (17 files) | Partially demonstrated |
| [SES-104](SES-104-foundgine-test-mapping.md) | Future implementation gap matrix | `conformance/known-gaps.json` | — | Fully demonstrated |
| [SES-105](SES-105-conformance-vectors.md) | Portable conformance vectors | — | — (benchmarks/ directories are performance benchmarks, not conformance vectors) | Not implemented |

**Cross-cutting notes:**

- Provider coverage is concentrated on PostgreSQL, an in-memory provider, and (for retrieval only) Elasticsearch/pgvector. Every "provider-specific" row above reflects this — the standard's provider-neutrality goal is not yet demonstrated across materially different provider kinds.
- The security/adversarial test surface is the deepest and most mature part of the implementation; the least mature areas are the cross-implementation/interoperability layers (SES-020, SES-022, SES-023, SES-101, SES-105), which by nature require more than one implementation to demonstrate.
- Four stray `*.migration-backup` files exist alongside their live counterparts under `tests/Foundgine.Semantics.Tests`, `tests/Foundgine.Planning.Tests`, and `tests/Foundgine.Aot.Tests`. Repository hygiene, not a conformance gap.

---

← [SES-105 — Portable Semantic Execution Conformance Vectors](SES-105-conformance-vectors.md) · [Standard Index](README.md) · *(end of family)*