# Conformance and verification

> **In plain words.** Source code shows what has been implemented. Tests show what has been exercised. A successful test run proves only the behavior and environment covered by that run.

## Status vocabulary

Use explicit evidence labels when describing maturity:

| Label | Meaning |
|---|---|
| **implemented** | The implementation exists in source. |
| **test-defined-not-run** | A test or scenario exists, but no passing execution evidence is available. |
| **verified (scoped)** | A named test passed in a stated environment; do not generalize beyond that scope. |
| **certified** | The stated CI/runtime gate passed for the named platform and version. |

Do not infer a passing test from a test class, workflow definition, source file, or package declaration. Do not infer cross-language parity from similar API names or matching generated text.

## Shared cross-language test data

Reusable vectors and scenarios live in [`src/conformance/`](https://github.com/Foundgine/Foundgine/blob/main/src/conformance). Their README documents the generators, consumers, canonicalization rules and relevant test commands. Change the generator and regenerate the JSON; do not edit generated vectors by hand.

The shared data covers warrant canonicalization and signatures, delegation, approval envelopes, semantic query behavior, MCP sessions and server-sent events, and tenant-first authorization scenarios. Each fixture's presence is not evidence that every runtime has executed it successfully.

## ORM provider evidence

- .NET: [`src/csharp/conformance/orm/provider-conformance-matrix.json`](https://github.com/Foundgine/Foundgine/blob/main/src/csharp/conformance/orm/provider-conformance-matrix.json) records provider capability and evidence status.
- Java: [`src/java/conformance/orm-jpa-parity-plan.json`](https://github.com/Foundgine/Foundgine/blob/main/src/java/conformance/orm-jpa-parity-plan.json) records the JPA capability plan; validate its structure with [`validate-orm-jpa-parity-plan.py`](https://github.com/Foundgine/Foundgine/blob/main/src/java/conformance/validate-orm-jpa-parity-plan.py).

Keep translation, runtime/database execution, and AOT evidence separate. In particular, do not describe an ORM capability as verified merely because its translator or test exists.

## Security and release evidence

Security behavior is exercised by the dedicated test projects and red-team harnesses under `src/csharp/security/`, `src/csharp/tests/`, `src/java/security/` and the Java Maven modules. The repository workflows under [`.github/workflows/`](https://github.com/Foundgine/Foundgine/blob/main/.github/workflows) define automated gates. Check the actual run result and environment before making a release or certification claim. The current C# PostgreSQL test opt-in gaps are documented in [PostgreSQL E2E](../POSTGRES-E2E.md) and the [Supply Chain PenTest fixture README](https://github.com/Foundgine/Foundgine/blob/main/src/csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.PenTest/README.md).

### Known approval/audit atomicity gap

Approval creation precedes the `ApprovalRequested` audit in both runtime governors. If the audit sink fails after the approval record is created, the caller may receive an error without receiving the approval result, leaving an orphaned pending approval. transactional approval+audit/outbox transitions are still required to make this state change atomic. The current synchronous audit-before-return behavior fails closed, but it does not make approval persistence and audit persistence one transaction.

For the broader implementation inventory, see [Source coverage](../SOURCE-COVERAGE.md). For test layers and local commands, see [Testing](../TESTING.md).
