# Warrant lifecycle enforcement — checkpoint 3

## Scope

This increment connects the existing warrant trust, delegation-chain and revocation primitives to the C# read execution pipeline. It does not introduce a second warrant format or replace the existing canonicalization, signature, replay or delegation primitives.

## Enforcement changes

- `SecurityExecutionContext` can carry an untrusted root-to-leaf `DelegationChain`.
- Root warrants continue through `SecurityWarrantVerifier`.
- Delegated warrants require an explicitly configured `ExpectedWarrantIssuer`, a complete chain ending in the presented warrant, and an `ISecurityWarrantDelegationTrustResolver`.
- The runtime invokes `SecurityWarrantExecutionTrust.Verify`, which validates the chain shape, every signature, each parent-child edge, issuer delegation authority, attenuation and validity windows.
- Warrant-backed discovery and read execution fail closed if `ISecurityWarrantRevocationStore` is not configured.
- Revocation checks include the presented warrant and every ancestor digest in the warrant's delegation path.
- The runtime revalidates trust and revocation immediately before provider dispatch. It captures the revocation sequence and rejects the result if the sequence changes during provider execution.

## Cache lifecycle changes

- The memory provider-plan cache uses single-flight compilation and bounded FIFO admission.
- Failed compilations are not admitted.
- Replacement, eviction and admission bookkeeping are coordinated to prevent stale FIFO entries from removing a newer replacement.
- `INamespacedProviderPlanCache` provides permanent namespace retirement.
- `FoundgineEngine.InvalidateProviderPlanCache()` retires only the engine's namespace. The host must stop routing requests and drain in-flight work first.
- Cache retirement does not clear replay records, revocation records or signing keys.

## Deliberately not adopted

The supplied checkpoint 2 changed the provider-plan partition from the full warrant digest to an authority-equivalence digest. That optimization is not included. The current implementation retains the conservative per-warrant partition until additional evidence demonstrates that provider-compiled plans cannot contain subject-, request- or execution-specific values and that cross-issuance reuse is safe for every provider.

## Operational requirements and limitations

1. Production hosts must provide a durable, shared `ISecurityWarrantRevocationStore`. `HighAssurance` supplies an in-memory implementation only as a local-development default.
2. Delegation-enabled hosts must provide a trusted `ISecurityWarrantDelegationTrustResolver` and attach the complete root-to-leaf chain to the security context.
3. The current revocation-store contract exposes a monotonic sequence but no freshness/health contract. A distributed implementation must fail closed on unavailable or stale backing state; this checkpoint cannot independently prove freshness.
4. A sequence change during provider execution is detected after the provider returns, and the result is rejected. This does not roll back side effects already committed by the provider. Atomic revocation-versus-side-effect guarantees require provider transaction coordination or a revocation lease/transaction protocol.
5. This increment wires the read `FoundgineEngine`. The separate `FoundgineMutationEngine` still needs an equivalent revocation/delegation integration pass.
6. The .NET SDK was unavailable in the implementation environment. No .NET build, xUnit run, database integration test or end-to-end suite is claimed as passing.

## Verification performed

- Both input ZIP archives passed their ZIP integrity checks before modification.
- The current repository's C# project layout and existing warrant trust/revocation primitives were inspected.
- Regression coverage was added for cache namespace retirement and an in-flight compilation racing retirement.
- Existing revocation tests already cover leaf/ancestor revocation and sequence changes; the read runtime now invokes those guards.
- Compilation and test execution remain pending in a .NET 10 environment.
