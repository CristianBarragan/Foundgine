# SES-100 — Forward Conformance Testing Specification
← [SES-026 — Proof, Attestation, and Verification](SES-026-proof-and-attestation.md) · [Standard Index](README.md) · [SES-101 — Test Architecture, Fixtures, and Independent Oracles](SES-101-test-architecture-and-fixtures.md) →

---

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

Every L0–L26 layer MUST have positive, negative, boundary, malformed-input, adversarial, and regression tests appropriate to its contract. A layer is not considered covered because another layer's E2E test happens to pass through it.

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

> **Resolved (`SES-GAP-06`, see SES-104 §5):** these `L#` labels now follow the same `L#=SES-0##` convention as `conformance/requirements.json`. The two layers that previously had no correctly-labeled section — L2 (SES-002, Canonical Lifecycle) and L7 (SES-007, Logical Traversal) — now have their own test requirements below, and the sections that were shifted by two (old L6–L16, which actually tested SES-008 through SES-018) have been renumbered to match their real subject matter. This document also now defines L17–L26 (`SES-GAP-02`), closing the range that `scripts/verify-conformance.py` checks.

### L0 — terminology and conformance
- **L0-T01**: normative language and requirement identifiers are machine-readable.
- **L0-T02**: claimed profile is explicitly declared.
- **L0-T03**: incompatible standard/profile versions are rejected.
- **L0-T04**: conformance manifest distinguishes implemented from proven.

### L1 — architectural model and trust boundaries
- **L1-T01**: trust domains (untrusted caller input, trusted semantic context, provider boundary) are explicit and independently identifiable.
- **L1-T02**: caller-controlled authority fields cannot override trusted context.
- **L1-T03**: serialization/coercion of lower-trust input cannot change semantic meaning.
- **L1-T04**: prompt injection or other lower-trust content does not create capability or authority.

### L2 — canonical semantic execution lifecycle
- **L2-T01**: an equivalent logical ordering of lifecycle stages (intent → trusted context → graph construction → retrieval → resolution → authorization → planning → provenance binding → IR compilation → provider gate → execution → result) is observable for every execution trace.
- **L2-T02**: no stage may be skipped or reordered in a way that lets an unauthorized or unresolved operation execute.
- **L2-T03**: partial or failed lifecycle traces terminate in a defined, fail-closed state rather than an undefined one.
- **L2-T04**: replaying a captured lifecycle trace against an equivalent implementation reproduces the same stage sequence and terminal state.

### L3 — semantic contract
- **L3-T01**: semantic identities are deterministic.
- **L3-T02**: contract snapshots are immutable/versioned.
- **L3-T03**: structural metadata cannot silently grant semantic authority.
- **L3-T04**: contract evolution preserves declared compatibility rules.

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

### L6 — retrieval and resolution
- **L6-T01**: exact resolution is deterministic.
- **L6-T02**: ambiguous resolution abstains according to policy.
- **L6-T03**: retrieval evidence cannot grant authority.
- **L6-T04**: poisoned aliases/metadata cannot redirect authorization.

### L7 — logical traversal and path expansion
- **L7-T01**: maximum depth, branching factor, and visited-node limits are enforced for every traversal.
- **L7-T02**: cycles and recursive paths are detected and bounded rather than followed indefinitely.
- **L7-T03**: traversal cannot cross tenant boundaries via a followed relationship/edge.
- **L7-T04**: fan-out from a single traversal step cannot bypass the resource or authorization scope declared for the originating operation.

### L8 — authorization
- **L8-T01**: whole-operation authorization is enforced.
- **L8-T02**: tenant/resource boundaries cannot be widened.
- **L8-T03**: delegation and obligations are enforced where supported.
- **L8-T04**: execution-time revalidation detects stale authority.

### L9 — authorization provenance
- **L9-T01**: provenance binds to exact semantic identity.
- **L9-T02**: altered provenance fails verification.
- **L9-T03**: stale/revoked provenance fails according to freshness policy.
- **L9-T04**: provenance cannot be substituted across tenants/principals.

### L10 — planning and rewrites
- **L10-T01**: safe rewrites preserve semantics.
- **L10-T02**: security-preserving rewrites preserve authorization scope.
- **L10-T03**: failed rewrite proof prevents unsafe execution.
- **L10-T04**: cost-based choices respect resource budgets.

### L11 — Execution IR
- **L11-T01**: invalid IR is rejected before provider compilation.
- **L11-T02**: IR preserves semantic and authorization provenance.
- **L11-T03**: parameter binding cannot widen scope.
- **L11-T04**: IR serialization/version incompatibility fails closed.

### L12 — provider boundary
- **L12-T01**: provider capabilities are declared and verified.
- **L12-T02**: unsupported semantics cannot silently degrade.
- **L12-T03**: provider execution preserves required security predicates.
- **L12-T04**: physical result semantics satisfy the declared contract.

### L13 — mutations
- **L13-T01**: mutation authorization covers all affected resources.
- **L13-T02**: atomicity/rollback guarantees are verified.
- **L13-T03**: idempotency and replay behavior are deterministic.
- **L13-T04**: concurrency conflicts cannot create unauthorized state.

### L14 — plan cache
- **L14-T01**: cache keys include required semantic/security dimensions.
- **L14-T02**: stale policy/contract entries are rejected.
- **L14-T03**: cache poisoning cannot broaden authority.
- **L14-T04**: cache reuse remains tenant-isolated.

### L15 — transports and agents
- **L15-T01**: transports converge on equivalent semantic requests.
- **L15-T02**: tool descriptions cannot create authority.
- **L15-T03**: agent delegation is bounded by trusted authority.
- **L15-T04**: transport-specific shortcuts cannot bypass security gates.

### L16 — resource governance
- **L16-T01**: input complexity is bounded.
- **L16-T02**: graph/retrieval/planning expansion is bounded.
- **L16-T03**: provider resource budgets are enforced.
- **L16-T04**: cancellation and budget exhaustion stop safely.

### L17 — evidence and observability
- **L17-T01**: execution lineage is reconstructable.
- **L17-T02**: sensitive data is redacted according to policy.
- **L17-T03**: evidence cannot be used as authority.
- **L17-T04**: error classes distinguish security, semantic, resource, and operational failure.

### L18 — AOT/generated metadata
- **L18-T01**: generated identities are deterministic.
- **L18-T02**: generated and runtime semantics remain equivalent.
- **L18-T03**: stale generated artifacts are rejected.
- **L18-T04**: generator/version changes are compatibility-checked.

### L19 — future development roadmap
- **L19-T01**: every roadmap backlog item has a stable identifier in the machine-readable gap registry.
- **L19-T02**: every backlog item carries an explicit, current status drawn from a closed set of allowed values.
- **L19-T03**: a CI check fails when a backlog item referenced as resolved in prose has no corresponding registry entry.
- **L19-T04**: this requirement class MAY be satisfied by process/documentation conformance rather than an executable test; profiles MUST declare which applies.

### L20 — formal data model
- **L20-T01**: security- and meaning-relevant artifacts validate against the declared typed schema.
- **L20-T02**: schema validation rejects artifacts violating stated invariants (e.g. required fields, cardinality, referential integrity).
- **L20-T03**: schema evolution is versioned and old/new artifacts are distinguishable.
- **L20-T04**: an artifact that fails schema validation cannot proceed to later lifecycle stages.

### L21 — state machines
- **L21-T01**: every lifecycle/security-relevant entity has a defined state machine with an explicit initial and terminal set of states.
- **L21-T02**: attempted transitions outside the defined transition table are rejected.
- **L21-T03**: no reachable state is undefined or ambiguous with respect to authorization implications.
- **L21-T04**: concurrent transition attempts on the same entity resolve deterministically or are serialized.

### L22 — wire protocol
- **L22-T01**: cross-transport exchange of semantic artifacts uses the defined transport-neutral encoding, or an encoding independently verified equivalent to it.
- **L22-T02**: malformed wire payloads are rejected before semantic processing.
- **L22-T03**: wire-level replay or tampering is detectable.
- **L22-T04**: protocol version mismatches fail closed rather than degrading silently.

### L23 — versioning and compatibility
- **L23-T01**: implementations negotiate a mutually supported standard/profile version before exchanging semantic artifacts.
- **L23-T02**: incompatible versions are rejected rather than partially interpreted.
- **L23-T03**: backward-compatible changes are distinguishable from breaking changes in the version identifier.
- **L23-T04**: a deprecated version's sunset behavior is explicit and testable.

### L24 — authority and delegation
- **L24-T01**: delegated or attenuated authority is representable in the authority algebra.
- **L24-T02**: delegation cannot exceed the delegator's own authority (no privilege escalation via delegation).
- **L24-T03**: delegated authority is independently verifiable at the point of use, not merely asserted by the delegate.
- **L24-T04**: revoking a delegation is observable and enforced at the next point of use.

### L25 — provider capability profiles
- **L25-T01**: providers declare a capability/fidelity profile before executing against it.
- **L25-T02**: declared capabilities are independently verifiable against observed execution behavior.
- **L25-T03**: a provider claiming a capability it does not actually support fails verification rather than silently degrading.
- **L25-T04**: capability profile mismatches between planning-time assumptions and provider execution fail closed.

### L26 — proof and attestation
- **L26-T01**: security- and semantic-relevant claims are expressible in a defined proof/attestation format.
- **L26-T02**: an attestation can be independently verified without trusting the component that produced it.
- **L26-T03**: tampering with an attested artifact invalidates the attestation.
- **L26-T04**: expired or revoked attestations fail verification.

## 9. Cross-layer requirements

- **X-T01**: no transformation widens semantic scope.
- **X-T02**: no transformation widens authorization scope.
- **X-T03**: no operation crosses tenant boundaries without explicit authority.
- **X-T04**: equivalent transports produce equivalent authorization semantics.
- **X-T05**: optimized and reference plans are equivalent where claimed.
- **X-T06**: cached and uncached executions preserve security semantics.
- **X-T07**: independent providers preserve required semantic guarantees.
- **X-T08**: every security-boundary failure fails closed.

---

← [SES-026 — Proof, Attestation, and Verification](SES-026-proof-and-attestation.md) · [Standard Index](README.md) · [SES-101 — Test Architecture, Fixtures, and Independent Oracles](SES-101-test-architecture-and-fixtures.md) →
