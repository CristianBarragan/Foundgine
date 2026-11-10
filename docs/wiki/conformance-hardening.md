# Conformance and hardening status

> **In plain words.** Two questions get asked of every feature: *is it built?* and *has a real run proven it?* This page is the honest scoreboard. It also lists the "same answer in C# and Java" test files and the numbered hardening steps (71 to 78) that closed specific gaps. Where a claim has no real run behind it, it says so.

## Status vocabulary

| Word | Meaning |
|---|---|
| **implemented** | Code exists |
| **implemented-unverified** | Code exists; real build/run evidence is missing |
| **verified (scoped)** | Proven in a named environment (for example Java against live PostgreSQL 16) |
| **certified** | Proven by actual CI/runtime evidence for the stated platform |
| **test-defined-not-run** | A test exists but has not been executed; not a pass |

Release rules from the plan: no parity claim from matching generated text; no snapshot-consistency claim from a cursor fingerprint; no audit-durability claim from an in-memory or queued sink without a flush/acknowledgement contract; no cache may keep an authorization decision across requests; no benchmark report is valid with zero telemetry samples.

## Cross-language vectors (`src/conformance`)

| File | Proves |
|---|---|
| `warrant-canonicalization.json` | Both runtimes produce identical canonical warrant bytes and digest (spec `warrant-canonical-form-v1`) |
| `warrant-signatures.json` | RSA PKCS#1 v1.5 / SHA-256 signatures reproduce byte for byte; 19 accept/reject cases (test-only keys) |
| `delegation-scenarios.json` | Delegation attenuation and chain validation, 32 scenarios, identical error messages |
| `approval-envelope-vectors.json` | Encrypted approval envelope: 3 seal and 12 reject vectors |
| `tenant-first-gate-scenarios.json` | Nine scenarios for the tenant-first gate (not yet loaded by the tests, which mirror them by hand) |

Vectors come from independent Python generators in `src/conformance/tools`. Never edit the JSON by hand; change the generator and regenerate. Canonical form notes: UTF-8, no whitespace, UTF-16 code-unit ordering, seven-digit UTC timestamps, scale-preserving decimals. Digests of older C# warrants containing `+ & < > '` or non-ASCII text changed when the serializer stopped escaping them; re-issue such warrants.

## Hardening steps

| Step | What changed | Evidence |
|---|---|---|
| 71 | Benchmark metrics collector no longer hides Docker failures; zero samples now fail the run; runners propagate the failure | Script change; new run still needed |
| 72 | `BufferedDurableAuditLog` in both runtimes: bounded queue, lossless backpressure, acknowledged writes, failures propagate, drain on close | Matching tests |
| 73 | Audit buffer metrics (depth, accepted, persisted, failed, waits); governance order verified fail-closed | Observational only |
| 74 | `IAtomicFoundgineGatewayApprovalStore` required for approval creation | In-memory stores only |
| 75 | `PostgresFoundgineGatewayApprovalStore`, migration `004_gateway_approvals.sql` | implemented-unverified |
| 76 | Integration suites; six defects fixed (outbox key leak, reserved tenant, digest/id validation, UTC normalisation, event-id format, expiry purge) | **Java verified** against PostgreSQL 16; .NET implemented-unverified |
| 77 | Approval encryption parity (AES-256-GCM, shared vectors) | Java verified by compile + vectors; .NET unrun |
| 78 | Tenant-first gate before name resolution; Java warrant-fingerprint serialization defect fixed | Java 9/9 and runtime 40/40 with `javac`/JUnit (not Maven); .NET shim-run only |

Intent fingerprints for warrant-bearing Java requests now embed the authority cache partition; do **not** compare intent fingerprints across runtimes.

## Open items

1. Telemetry and load evidence: identical benchmark tiers for both runtimes.
2. Tenant scope survives every planner rewrite and provider boundary (shared scenario still to add); no-context requests still reach the resolver with detailed error text.
3. Transactional approval+audit outbox for the tool-call governor; concrete durable audit sinks; crash/restart recovery tests; only then an async governor API.
4. Safe structural plan cache (tenant-neutral shapes, bounded, versioned keys; never an allow).
5. Short-lived lease exchange and plan pinning, revalidated at each execution boundary.
6. Durable human-in-the-loop suspension bound to plan digest, tenant, actor, expiry and nonce.
7. AOT/type conformance vectors (nullability, nested generics, enums, time types) with Native AOT and Native Image runs.
8. Sandbox deployment recipes (gVisor, containers, Wasm); the engine's invariants are in-process, not a process sandbox.
9. Envelope v2 binding ciphertext to subject, tenant and digest; slow-codec lock holding; clock skew on expiry.
10. ORM: run the EF Core and JPA matrices against real databases; aggregate cursor pagination stays off until then.

## Where to look

`src/DUAL-RUNTIME-HARDENING-PLAN`, `src/conformance/README`, `src/java/ORM-JPA-PARITY-PLAN`, `src/csharp/conformance/orm/provider-conformance-matrix.json`, `src/csharp/ci`.
