# SES-100 — Forward Conformance Testing Specification

## Status and purpose

**Status:** Draft 1.0 — Forward-looking specification and gap analysis.

This document is **not a reverse-engineering report** and is not a claim that the reference implementation already satisfies every requirement. It defines the target architecture for a production-grade, provider-neutral Semantic Execution System (SES), identifies the capabilities that remain to be specified or developed, and gives concrete acceptance criteria for future implementations.

The reference implementation may be used as an informative experiment, but **the standard is the authority**. Where the implementation and this specification differ, the implementation is considered incomplete until it is brought into conformance or the standard is deliberately amended.

Normative terms **MUST**, **MUST NOT**, **REQUIRED**, **SHALL**, **SHALL NOT**, **SHOULD**, **SHOULD NOT**, **RECOMMENDED**, and **MAY** have their usual standards meaning.

## 1. Principle

Testing is not a postscript. Every normative requirement MUST have an independently identifiable validation method.

## 2. Mandatory test classes

Each implementation MUST maintain, as applicable:

- unit tests;
- contract tests;
- property-based tests;
- fuzz tests;
- negative/security tests;
- differential tests;
- integration tests;
- concurrency tests;
- fault-injection tests;
- serialization/compatibility tests;
- resource/DoS tests;
- end-to-end tests;
- provider-fidelity tests.

## 3. Layer requirements

Every L0–L16 layer MUST have positive, negative, boundary, malformed-input, adversarial, and regression tests appropriate to its contract. A layer is not considered covered because another layer's E2E test happens to pass through it.

## 4. Hard rule

Line coverage, branch coverage, mutation score, or test count MUST NOT be used as substitutes for requirement coverage.

## 5. Future requirement matrix

Each requirement MUST map to:

`Requirement → invariant → test ID → fixture → oracle → expected result → implementation → provider/profile → CI gate`

## 6. Critical cross-layer tests

At minimum the future suite MUST prove:

- semantic non-widening;
- authorization non-widening;
- tenant non-crossing;
- transport equivalence;
- optimizer equivalence;
- cache equivalence;
- provider equivalence;
- fail-closed behavior;
- generated/runtime parity.

## 7. Development gap

The next stage is to map every normative requirement to exact test methods and identify missing tests rather than merely listing existing test files.


## 8. Layer-by-layer mandatory requirement identifiers

The following identifiers are normative anchors. Implementations MUST map each identifier to an executable test or an explicitly declared profile exclusion.

### L0 — terminology and conformance
- **L0-T01**: normative language and requirement identifiers are machine-readable.
- **L0-T02**: claimed profile is explicitly declared.
- **L0-T03**: incompatible standard/profile versions are rejected.
- **L0-T04**: conformance manifest distinguishes implemented from proven.

### L1 — semantic contract
- **L1-T01**: semantic identities are deterministic.
- **L1-T02**: contract snapshots are immutable/versioned.
- **L1-T03**: structural metadata cannot silently grant semantic authority.
- **L1-T04**: contract evolution preserves declared compatibility rules.

### L2 — intent boundary
- **L2-T01**: malformed intent is rejected safely.
- **L2-T02**: caller-controlled authority fields cannot override trusted context.
- **L2-T03**: serialization/coercion cannot change semantic meaning.
- **L2-T04**: prompt injection does not create capability or authority.

### L3 — retrieval and resolution
- **L3-T01**: exact resolution is deterministic.
- **L3-T02**: ambiguous resolution abstains according to policy.
- **L3-T03**: retrieval evidence cannot grant authority.
- **L3-T04**: poisoned aliases/metadata cannot redirect authorization.

### L4 — Semantic Operation Graph
- **L4-T01**: canonical graph identity is deterministic.
- **L4-T02**: security-relevant graph mutations change identity.
- **L4-T03**: graph validation rejects malformed topology.
- **L4-T04**: graph serialization is canonical and interoperable.

### L5 — semantic algebra
- **L5-T01**: required algebraic identities hold.
- **L5-T02**: null/duplicate/order semantics are preserved.
- **L5-T03**: unsafe rewrites are rejected.
- **L5-T04**: nondeterministic/side-effecting operations are classified correctly.

### L6 — authorization
- **L6-T01**: whole-operation authorization is enforced.
- **L6-T02**: tenant/resource boundaries cannot be widened.
- **L6-T03**: delegation and obligations are enforced where supported.
- **L6-T04**: execution-time revalidation detects stale authority.

### L7 — provenance
- **L7-T01**: provenance binds to exact semantic identity.
- **L7-T02**: altered provenance fails verification.
- **L7-T03**: stale/revoked provenance fails according to freshness policy.
- **L7-T04**: provenance cannot be substituted across tenants/principals.

### L8 — planning
- **L8-T01**: safe rewrites preserve semantics.
- **L8-T02**: security-preserving rewrites preserve authorization scope.
- **L8-T03**: failed rewrite proof prevents unsafe execution.
- **L8-T04**: cost-based choices respect resource budgets.

### L9 — Execution IR
- **L9-T01**: invalid IR is rejected before provider compilation.
- **L9-T02**: IR preserves semantic and authorization provenance.
- **L9-T03**: parameter binding cannot widen scope.
- **L9-T04**: IR serialization/version incompatibility fails closed.

### L10 — provider boundary
- **L10-T01**: provider capabilities are declared and verified.
- **L10-T02**: unsupported semantics cannot silently degrade.
- **L10-T03**: provider execution preserves required security predicates.
- **L10-T04**: physical result semantics satisfy the declared contract.

### L11 — mutations
- **L11-T01**: mutation authorization covers all affected resources.
- **L11-T02**: atomicity/rollback guarantees are verified.
- **L11-T03**: idempotency and replay behavior are deterministic.
- **L11-T04**: concurrency conflicts cannot create unauthorized state.

### L12 — plan cache
- **L12-T01**: cache keys include required semantic/security dimensions.
- **L12-T02**: stale policy/contract entries are rejected.
- **L12-T03**: cache poisoning cannot broaden authority.
- **L12-T04**: cache reuse remains tenant-isolated.

### L13 — transports and agents
- **L13-T01**: transports converge on equivalent semantic requests.
- **L13-T02**: tool descriptions cannot create authority.
- **L13-T03**: agent delegation is bounded by trusted authority.
- **L13-T04**: transport-specific shortcuts cannot bypass security gates.

### L14 — resource governance
- **L14-T01**: input complexity is bounded.
- **L14-T02**: graph/retrieval/planning expansion is bounded.
- **L14-T03**: provider resource budgets are enforced.
- **L14-T04**: cancellation and budget exhaustion stop safely.

### L15 — evidence and observability
- **L15-T01**: execution lineage is reconstructable.
- **L15-T02**: sensitive data is redacted according to policy.
- **L15-T03**: evidence cannot be used as authority.
- **L15-T04**: error classes distinguish security, semantic, resource, and operational failure.

### L16 — AOT/generated metadata
- **L16-T01**: generated identities are deterministic.
- **L16-T02**: generated and runtime semantics remain equivalent.
- **L16-T03**: stale generated artifacts are rejected.
- **L16-T04**: generator/version changes are compatibility-checked.

## 9. Cross-layer requirements

- **X-T01**: no transformation widens semantic scope.
- **X-T02**: no transformation widens authorization scope.
- **X-T03**: no operation crosses tenant boundaries without explicit authority.
- **X-T04**: equivalent transports produce equivalent authorization semantics.
- **X-T05**: optimized and reference plans are equivalent where claimed.
- **X-T06**: cached and uncached executions preserve security semantics.
- **X-T07**: independent providers preserve required semantic guarantees.
- **X-T08**: every security-boundary failure fails closed.
