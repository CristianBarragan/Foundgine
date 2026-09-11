# Java test parity tracker

The Java port is being aligned to the C# test suite by behavior and boundary, not by mechanically translating syntax.

| C# test project | C# test files | Java status |
|---|---:|---|
| Foundgine.Aot.Tests | 3 | Partial: generator identity coverage exists |
| Foundgine.E2E.Tests | 67 | In progress: Advanced SupplyChain integration path started |
| Foundgine.GraphQL.HotChocolate.Tests | 26 | Transport-specific; Java parity will use Java transport adapters rather than Hot Chocolate |
| Foundgine.HighAssurance.Tests | 1 | Partial: Advanced high-assurance mutations covered |
| Foundgine.InMemory.Tests | 3 | Partial: provider execution coverage exists; broader parity pending |
| Foundgine.Intent.Json.Tests | 4 | Partial: intent types/serialization coverage exists |
| Foundgine.MCP.Tests | 8 | Partial: Advanced MCP facade integration coverage exists |
| Foundgine.Planning.Tests | 38 | Partial: planner/rewrite core coverage exists |
| Foundgine.Postgres.Vector.Tests | 3 | Pending PostgreSQL/vector parity |
| Foundgine.Security.Authority.Tests | 17 | Pending broader authority/security parity |
| Foundgine.Security.Tests | 23 | Pending broader adversarial parity |
| Foundgine.Semantics.Tests | 52 | In progress: identity, aliases, grounding, mutation, planning, model lifecycle and query contracts mirrored |
| Foundgine.Sql.Tests | 1 | Partial: SQL mutation normalization coverage exists |

The Java suite currently contains **40 test classes**. New parity tests are named `*ParityTest` where the purpose is to make the C# source test being mirrored explicit.

## Porting order

1. Core semantic/unit tests
2. Core planning and execution tests
3. Runtime unit tests and mutation security tests
4. Provider unit/conformance tests
5. Advanced SupplyChain integration tests
6. MCP integration/security tests
7. PostgreSQL integration tests
8. AOT generator/runtime parity tests
9. End-to-end and red-team suites
10. Benchmarks and deployment/pipeline integration

## Latest parity pass
- `Foundgine.Planning.Tests/Security/PlanSecurityInvariantTests.cs` -> `PlanSecurityInvariantParityTest`
- `Foundgine.Security.Tests/Penetration/WarrantTrustBoundaryPenetrationTests.cs` -> `WarrantTrustBoundaryParityTest`
- `Foundgine.MCP.Tests/McpBoundaryTests.cs` -> `McpBoundaryParityTest`
- `Foundgine.Aot.Tests/IdentityDeterminismTests.cs` -> `GeneratorSemanticIdentityParityTest`

### Latest parity pass
- `Foundgine.Semantics.Tests/SemanticAlgebraTests.cs` → `SemanticAlgebraParityTest`
- `Foundgine.Semantics.Tests/SemanticOperationGraphSafetyStep32Tests.cs` → `SemanticOperationGraphSafetyParityTest`
- `Foundgine.Semantics.Tests` authorization-composition invariant → `SemanticAuthorizationCompositionParityTest`

### Latest parity pass

- `SemanticApproximateRetrievalParityTest` — provider-neutral approximate retrieval, entity filtering, deterministic ordering/limits, request bounds.
- `SemanticReferenceGroundingParityTest` — relationship grounding, fail-closed traversal, ambiguity preservation.
- `SemanticLexicalResolverParityTest` — token budget refusal, competing meanings, cancellation fail-closed behavior.
- `SemanticMutationPlannerParityTest` — dependency ordering and returned-field validation.


### Latest parity pass

- `Foundgine.Security.Tests/Security/MutationExecutionSecurityGateTests.cs` / authorization revalidation invariants → `SecurityExecutionAuthorizationRevalidationParityTest`
- `Foundgine.Security.Tests/Penetration/WarrantTrustBoundaryPenetrationTests.cs` / replay and revocation invariants → `WarrantReplayAndRevocationParityTest`
- `Foundgine.Security.Tests/AdversarialAgentBoundaryTests.cs` / capability composition invariants → `SecurityCapabilityCompositionParityTest`

- Added provider parity coverage for InMemory provider plan boundaries and MCP mutation/security transport boundaries.

### Latest parity pass
- Added malicious JSON intent boundary tests mirroring C# `McpMaliciousIntentSecurityTests` at the Java serialization boundary.
- Covers security/provider authority injection, hostile tenant filters remaining untrusted input, and bounded malicious predicate structures.
