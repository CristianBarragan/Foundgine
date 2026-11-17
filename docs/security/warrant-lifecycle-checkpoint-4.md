# Warrant lifecycle checkpoint 4 — mutation execution parity

## Scope

This increment closes the warrant lifecycle gap identified in checkpoint 3: `FoundgineMutationEngine` previously verified the presented warrant and consumed replay state, but did not apply the read engine's delegation trust and revocation checks.

## Changes

- Added optional constructor dependencies for `ISecurityWarrantRevocationStore` and `ISecurityWarrantDelegationTrustResolver`.
- Wired both dependencies from `FoundgineOptions` and the dependency-injection service provider.
- Added root/delegated warrant trust validation to mutation planning. Delegated warrants require the complete root-to-leaf chain, an explicit expected root issuer, and a delegation trust resolver through `SecurityWarrantExecutionTrust`.
- Added fail-closed revocation validation. Warrant-backed mutation discovery/planning and execution now require a configured revocation store.
- Revalidate warrant trust and revocation immediately before consuming the replay record and dispatching the mutation provider.
- Capture the revocation sequence before provider dispatch; reject the returned result if revocation state changes during provider execution, and re-check the warrant after dispatch.
- Enforce the warrant subject and allowed-tenant constraints on the mutation security context.

## Execution ordering

1. Validate request resource limits.
2. Verify warrant signature/trust, delegation chain, expiry and revocation.
3. Authorize every requested mutation capability and allowed field.
4. Build and certify the exact mutation execution IR.
5. Revalidate warrant trust and revocation immediately before dispatch.
6. Consume the single-use replay record.
7. Execute the provider batch.
8. Verify the revocation sequence did not change and re-check revocation before returning the result.

## Important limitation

The post-dispatch revocation check detects a concurrent revocation and suppresses the successful result, but it cannot roll back a side effect already committed by a provider. Strong atomic revocation-versus-mutation semantics require provider transaction coordination or a transactional authorization fence. This checkpoint does not claim that guarantee.

The in-memory revocation store is suitable for tests and single-process scenarios, not as a distributed production authority. Production deployments need a shared durable store with consistency and freshness guarantees.

## Verification status

- Source-level integration and DI wiring reviewed.
- ZIP integrity checked after packaging.
- .NET build and xUnit execution not performed: the .NET SDK is not installed in the working environment.
- No production-verification claim is made.
