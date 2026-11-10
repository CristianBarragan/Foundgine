# Authorization, warrants and security

## Purpose

Foundgine separates semantic discovery from permission to execute. Authorization must be evaluated for the actual caller, operation, resource, tenant/scope, and relevant constraints. A resolved entity or a valid plan is never sufficient authority by itself.

## Security concepts

- **Principal/subject:** the caller identity established by the host or identity integration.
- **Security execution context:** trusted identity and scope information passed to runtime execution.
- **Authorization decision:** allow/deny plus any applicable constraints or evidence.
- **Warrant:** a bounded grant of authority represented as a verifiable security object. It may bind subject, scope, operations, validity, issuer, and delegation/trust information according to the configured contract.
- **Issuer and key resolver:** trusted components that create or validate warrant material.
- **Revocation and trust transitions:** mechanisms to invalidate or reduce previously issued authority.
- **Audit evidence:** records that explain which authority was presented and what decision occurred.

## Enforcement sequence

1. Authenticate the caller outside or at the host boundary.
2. Build a trusted execution context; do not accept tenant identity or privileged claims directly from arbitrary request fields.
3. Resolve intent without treating resolution as permission.
4. Request authorization for the precise operation and scope.
5. Validate warrant integrity, issuer trust, validity, delegation, revocation, and context binding as applicable.
6. Enforce the decision at the execution/provider boundary, including row/tenant predicates where required.
7. Record appropriate audit evidence.

## Tenant isolation

Tenant scope must be derived from a trusted identity/security context. A client must not be able to switch tenants by changing a query parameter or semantic filter. Scope should be bound consistently to authorization, plan identity, cursor identity, cache keys, event visibility, and execution context.

## Warrant lifecycle

A warrant is not simply a bearer string. Its security depends on canonicalization, signature/key resolution, issuer trust, expiry, scope, delegation rules, revocation, and replay protections. Treat warrant digests and cache partitions as security-sensitive. Key rotation should be planned because it can affect validation of previously issued warrants and persisted evidence.

## Fail-closed behavior

Missing authorization configuration, malformed or unverifiable warrants, unknown issuer keys, expired/revoked authority, scope mismatch, and unsupported enforcement paths must not silently fall back to allow. A provider that cannot enforce the required predicate must reject the operation.

## Audit and privacy

Audit enough to reconstruct the decision and execution chain, but avoid logging credentials, private keys, raw bearer tokens, or unnecessary personal data. Hashes/digests should be domain-separated and keyed where guessing attacks are a concern.

## Where to look in source

- C#: `Foundgine.Core/Semantic/Security`, `Security/Warrants`, `Foundgine.Runtime/ControlPlane`, `Foundgine.Runtime/Execution`, and Keycloak extension security folders.
- Java: `foundgine-core/.../semantic/security`, `foundgine-runtime` security/governance packages, and `foundgine-extensions/.../keycloak`.
- Existing guides: [Security](../SECURITY), [Authorization](../AUTHORIZATION).
