# Foundgine cross-language conformance

This directory contains shared fixtures used to compare the .NET and Java implementations. A fixture defines inputs and expected results; it does not prove that either runtime passed the corresponding test. Check the actual test run and runtime/database environment before making a conformance claim.

## Fixture index

| File | Contract | Generator / maintenance | Consumers |
|---|---|---|---|
| `approval-envelope-vectors.json` | Approval payload envelope v1: deterministic seal/open cases, malformed envelopes and authentication failures | `tools/generate_approval_envelope_vectors.py` | C# `ApprovalPayloadEnvelopeConformanceTests`; Java `ApprovalPayloadEnvelopeConformanceTest` |
| `delegation-scenarios.json` | Warrant attenuation and delegation-chain validation; 32 scenarios with expected exception text and digests | `tools/generate_delegation_scenarios.py` | C# and Java `WarrantDelegationConformance*` tests |
| `mcp-approval-token-golden.json` | Fixed `fgmcp1` RSA-SHA256 approval-token interoperability vector | Maintained as a fixed golden fixture; do not casually regenerate the key or token | C# MCP approval-token/key-rotation tests; Java `McpApprovalTokenInteropTest` and key-rotation tests |
| `mcp-session-scenarios.json` | MCP session creation, expiry, capacity and lifecycle behavior | `tools/generate_mcp_session_scenarios.py` | C# and Java `McpSessionConformance*` tests |
| `mcp-sse-scenarios.json` | MCP SSE replay-buffer ids, replay windows, eviction and live subscription behavior | `tools/generate_mcp_sse_scenarios.py` | C# and Java `McpSseConformance*` tests |
| `mcp-sse-transport-scenarios.json` | MCP SSE transport gate/status and per-owner/global limits | Shared fixture; no generator is currently checked in | C# and Java `McpSseTransportConformance*` tests |
| `semantic-query-vectors.json` | Fluent `SemanticQuery` builder operations and canonical rendering | `tools/generate_semantic_query_vectors.py` | C# and Java `SemanticQueryConformance*` tests |
| `tenant-first-gate-scenarios.json` | Tenant/subject gate expectations for request paths that could otherwise reveal schema names | Shared scenario file; no generator is currently checked in | Python source-contract check plus mirrored C# `TenantFirstGateTests` and Java `TenantFirstGateParityTest` |
| `warrant-canonicalization.json` | Canonical warrant bytes and digest (`warrant-canonical-form-v1`) | `tools/generate_warrant_vectors.py` | C# and Java `WarrantCanonicalizationConformance*` tests |
| `warrant-signatures.json` | Deterministic RSA PKCS#1 v1.5/SHA-256 signatures and verification cases | `tools/generate_signature_vectors.py` | C# and Java `WarrantSignatureConformance*` tests |

The tenant-first runtime tests currently mirror the scenario names and expectations in code; they do not load the JSON directly. The Python contract test checks that both runtimes contain the corresponding cases. Do not describe this as a fully data-driven test harness.

## Running the consumer tests

Run the language suites from the repository root:

```bash
dotnet test src/csharp/Foundgine.sln
(cd src/java && mvn -U test)
```

These are broad suite commands, not proof that every fixture is exercised in every run. Database-gated tests require their documented connection settings; see [`docs/POSTGRES-E2E.md`](../../docs/POSTGRES-E2E.md) for the C# PostgreSQL gates and [`src/java/BUILDING.md`](../java/BUILDING.md) for Java build and integration-test settings.

## Regenerating fixtures

For generated fixtures, change the generator and regenerate the JSON. Do not edit generated vectors by hand. The signature generator reuses the checked-in test keys unless `FOUNDGINE_REGENERATE_KEYS=1` is set. Those private keys are test-only; rotating them changes the golden signatures and requires a deliberate cross-runtime update.

Run generators from the repository root. The approval-envelope and signature generators require the Python `cryptography` package. Generators that write to standard output need explicit redirection; the signature generator must write to a temporary file because it reads the existing fixture to reuse its test keys:

```bash
python3 -m pip install cryptography
python3 src/conformance/tools/generate_approval_envelope_vectors.py > src/conformance/approval-envelope-vectors.json
python3 src/conformance/tools/generate_delegation_scenarios.py > src/conformance/delegation-scenarios.json
python3 src/conformance/tools/generate_mcp_session_scenarios.py
python3 src/conformance/tools/generate_mcp_sse_scenarios.py
python3 src/conformance/tools/generate_semantic_query_vectors.py
python3 src/conformance/tools/generate_warrant_vectors.py > src/conformance/warrant-canonicalization.json
python3 src/conformance/tools/generate_signature_vectors.py > src/conformance/warrant-signatures.json.tmp
mv src/conformance/warrant-signatures.json.tmp src/conformance/warrant-signatures.json
```

The fixed `mcp-approval-token-golden.json` is an interoperability fixture rather than output from one of the checked-in generators. Treat its key and token as test data, not production credentials.

## Warrant canonical form v1

- UTF-8, no whitespace, property order exactly as in the vectors.
- Strings: escape only `"` `\` `\n` `\r` `\t` `\b` `\f` and other C0 controls as `\u00xx` (lowercase hex). Everything else (non-ASCII, astral characters, `+ & < > '`, DEL) is emitted raw. Unpaired surrogates are an error.
- Ordering: UTF-16 code units (.NET `StringComparer.Ordinal`, Java `String.compareTo`), *not* code points. Grants sort by capability, operation, then scopes joined with U+001F. List fields are blank-filtered, de-duplicated and sorted; `delegationPath` keeps its order.
- Timestamps: UTC, `yyyy-MM-ddTHH:mm:ss.fffffffZ` (seven digits, literal `Z`).
- `maxResults` is a plain integer; `maxAmount` is the plain decimal with its scale preserved (`100.50` stays `100.50`). Absent values are `null`.

## Resolved divergences and notes

- Time precision: resolved. Java `SecurityWarrant` truncates `issuedAt`/`expiresAt` to 100 ns on construction (the canonical resolution and .NET tick size), so digests and comparisons agree. Unit-tested in Java only.
- Blank strings: resolved. Both runtimes use one explicit rule (`WarrantText`): empty, or only U+0009-U+000D and U+0020. Other Unicode spaces (U+00A0, U+2003, U+0085, U+001C...) are kept as real values. Previously .NET dropped NBSP-only and Java dropped U+001C-U+001F-only scope strings. Covered by a vector. Only grant/constraint normalization uses it; validation checks elsewhere (trust stores, replay store) still use the platform predicate.
- Digests of existing C# warrants whose strings contained `+ & < > ' ` or non-ASCII characters change, because the old C# serializer escaped them as `\uXXXX`. Re-issue such warrants; warrants with plain ASCII values are unaffected.
