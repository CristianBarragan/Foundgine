# Foundgine Keycloak integration

Optional identity integration inside `Foundgine.Extensions`. Keycloak authenticates people and services; Foundgine
still authorizes every operation with its own signed warrants. This code only *bridges* the two.

```
Keycloak access token ──► JWT validation (issuer, audience, signature, lifetime)
        │                         │
        │                  subject + tenant + roles
        ▼                         ▼
   (never forwarded)      FoundgineKeycloakWarrantIssuer ──► short-lived, single-use signed SecurityWarrant
                                                                │
                                                     SecurityExecutionContext ──► Foundgine runtime / gateway
                                                                                       │
                                                  FoundgineWarrantKeyResolver ◄────────┘ (verifies signature by key id)
```

## What it provides

- `AddFoundgineKeycloak` — Keycloak bearer validation, `realm_access`/`resource_access` roles projected to ASP.NET roles,
  and `KeycloakSecurityExecutionContextProvider` as the host's `ISecurityExecutionContextProvider`. It replaces the
  `HttpContextSecurityExecutionContextProvider` in `Gateway.MinimalApi`.
- `AddFoundgineKeycloakWarrants` — issuance, verification keys and the admin client, for worker/background hosts.
- `FoundgineWarrantKeyResolver` — the runtime's `ISecurityWarrantKeyResolver` (see *Wiring the runtime*).
- `AddRolePolicy` — e.g. the `workflow-approver` policy the Temporal approval endpoint uses.
- `KeycloakTemporalSecurityContextFactory` — resolves the Temporal worker's activity authority (`UseForTemporalWorker()`).
- `KeycloakAdminClient` + `AddKeycloakAdminOperations()` — customer-onboarding operations for Temporal (see *Onboarding*).
- `KeycloakServiceTokenProvider` — cached client-credentials token for operations calling other protected services.
- `FoundgineKeycloakProvider` — registers an `Identity` provider with the Foundgine provider registry.

## Security behaviour

- Tenant, subject and roles come only from the validated token. Missing tenant or no mapped grants → no context →
  callers fail as unauthenticated. The token itself is never stored or forwarded.
- Warrants are short-lived (default 5 min), RSA-signed (≥2048 bit) and constrained to the caller's tenant.
- **A fresh warrant is minted on every `GetSecurityExecutionContext()` call.** The runtime consumes each warrant's nonce
  once (replay protection), so reusing one warrant for a second execution in the same request or activity is rejected.
  The Temporal activity scope does the same: first call gets the pre-created context, later calls mint new ones.
- Use a mounted secret or KMS-backed `IFoundgineWarrantSigningKeyProvider`; `UseEphemeralWarrantKey()` is development only.

### Constraints and resource scopes (verified against `Foundgine.Core`)

- In `SecurityWarrantConstraints` an **empty list means "no restriction"** for that dimension (`Unrestricted` is all-empty).
  The default here sets only `AllowedTenants = [tenant]`, which allows that tenant and denies every other one.
  Set `ConstraintsFactory` to restrict fields, operations or amounts further.
- The runtime matches resource scopes **by exact string** (`requireResourceScopeMatch = true`). `"*"` is **not** a wildcard:
  a grant with `ResourceScopes: ["*"]` never matches and denies everything. Startup validation rejects it.
  Leave a grant's `ResourceScopes` **empty** for "any scope within the tenant", or list exact scope strings.
- The context's resource scope defaults to the tenant id; override with `ResourceScopeResolver`.

## Wiring the runtime

The runtime must be told which keys to trust. `FoundgineWarrantKeyResolver.Create(options, signingKey)` trusts this host's
own signing key plus everything in `TrustedWarrantPublicKeys` (key id → PEM text or path to a PEM file). `AddFoundgineKeycloak*`
also registers that resolver as an `ISecurityWarrantKeyResolver` service. `AddFoundgine`'s options lambda runs without a
service provider, so build the resolver beside it:

```csharp
var keycloak = new FoundgineKeycloakOptions();
builder.Configuration.GetSection("Foundgine:Keycloak").Bind(keycloak);
var signingKey = new PemWarrantSigningKeyProvider(keycloak.WarrantKeyId, File.ReadAllText(keycloak.WarrantPrivateKeyPemPath!));

builder.Services.AddFoundgine(o =>
{
    // ... model / metadata / authorization as before ...
    o.WarrantKeyResolver   = FoundgineWarrantKeyResolver.Create(keycloak, signingKey);
    o.ExpectedWarrantIssuer = keycloak.WarrantIssuer;          // "foundgine-keycloak-bridge" by default
    o.WarrantReplayStore    = sharedReplayStore;               // Redis/SQL-backed ISecurityWarrantReplayStore, not per-instance
    o.UseHighAssurance();
});
```

- **One key id per host.** Give the API and each worker their own `WarrantKeyId` and private key, and register every
  worker's *public* key under `TrustedWarrantPublicKeys` on the verifying host. A compromised worker's key can then be
  removed on its own. Sharing one key means any host holding it can mint warrants for every tenant.
- The resolver refuses private key material and keys under 2048 bits, and returns `null` for unknown key ids (the runtime rejects those).
- The default replay store (`HighAssurance`'s file-backed one) is for development only; a real deployment needs a shared store.

## Workers and authority

Worker activities run with `WorkerRoles` authority (not the user's) and the initiator as audit subject, bound to the
workflow's tenant. Keep that role least-privilege. Re-checking the user's *current* roles at activity time would need a
Keycloak Admin API lookup and is not done.

## Onboarding: Keycloak Admin operations for Temporal

`AddKeycloakAdminOperations()` registers five operations a process can use (see `Samples/Temporal.Worker/OnboardingProcess.cs`):

| Operation | Payload | Notes |
|---|---|---|
| `keycloak.create-tenant-group` | – | top-level group named after the tenant; output `{"groupId"}` |
| `keycloak.delete-tenant-group` | – | compensation |
| `keycloak.create-user` | `username`, `email`, `firstName`, `lastName`, `roles[]`, `requiredActions[]` | output `{"userId"}`; joins the tenant group if present |
| `keycloak.delete-user` | `username` | compensation |
| `keycloak.disable-user` | `username` | offboarding |

Safety properties (covered by tests against an in-memory Admin API):

- The tenant always comes from the workflow, never the payload; a payload cannot override it.
- Created users/groups are stamped with the creating workflow's id (`foundgine_provisioned_by`). Delete only removes
  objects of the same tenant that **the same workflow** created, so compensation can never remove a pre-existing account.
  A user/group that already exists but was not created by this workflow is a non-retryable conflict (never silently adopted).
- Refusals for "username taken" are identical whoever owns it, so other tenants' usernames are not disclosed.
- `roles` must be on `AdminAssignableRoles`, `requiredActions` on `AdminAllowedRequiredActions` (default: `UPDATE_PASSWORD`,
  `VERIFY_EMAIL`, `CONFIGURE_TOTP`). Anything else is rejected, so a payload cannot grant a privileged role.
- No passwords pass through workflow history. New users set their own via the `UPDATE_PASSWORD` required action.
- Retries are idempotent. 4xx and permission errors are non-retryable; 5xx/408/429 are retried by Temporal.

The worker needs `ServiceClientId`/`ServiceClientSecret` for a confidential service-account client whose account has the
`realm-management` client roles `manage-users`, `query-users`, `query-groups` and `view-realm` (the dev realm import grants these).
Admin API address is derived from `Authority` (`https://host/realms/<realm>` → `https://host/admin/realms/<realm>`) or set `AdminBaseUrl`.

## Keycloak setup checklist

1. Realm client for your API/UI with an **audience mapper** adding `foundgine-api` to access tokens.
2. A **user attribute mapper** emitting `tenant_id`.
3. **Declare `tenant_id` (and `foundgine_provisioned_by`) in the realm's User Profile with `view` and `edit` limited to `admin`.**
   On Keycloak 24+ an undeclared attribute is dropped and never reaches the token. Do **not** "fix" that by enabling
   unmanaged attributes with the default `ENABLED` policy: the tenant is what scopes every warrant, and users must not be
   able to read or change their own. (If you must use unmanaged attributes, use `ADMIN_EDIT` or `ADMIN_VIEW`.)
4. Realm roles matching `RoleGrants` keys; a confidential service-account client for workers (roles above).
5. `docker compose -f docker-compose.keycloak.yml up` imports a dev realm (`alice`/`alice`, tenant `acme`) with the
   admin-only user profile and a worker service account.

## Verification status

Checked by running the code against the real `Foundgine.Core` assemblies (issue → verify → authorize → replay) and a fake
Admin API (52 checks). **Not** verified: the dev realm import and the Admin API paths/permissions against a live Keycloak,
the `Authentication/` code against a restored `Microsoft.AspNetCore.Authentication.JwtBearer`, and the Temporal worker with `Temporalio`.
