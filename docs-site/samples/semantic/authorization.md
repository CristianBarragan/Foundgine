# SupplyChain semantic authorization cases

The SupplyChain sample is deliberately a mixed authorization laboratory. It demonstrates six independent policy boundaries and exercises them through an MCP client that treats every request as untrusted.

## 1. Entity policy

Entity policy answers whether the semantic resource itself is available. `ComplianceIncident` is visible to analysts and supply-chain managers, but not customers.

## 2. Field policy

Field policy narrows an otherwise readable entity. `InventoryLot.Quarantined` is operationally sensitive and `Supplier.RiskScore` is restricted to analyst/manager roles.

## 3. Relationship policy

Relationship policy controls traversal. Even if the source entity is readable, a denied relationship removes the child subtree. `Supplier.incidents` is restricted.

## 4. Conditional policy

Tenant-owned resources use a provider-independent predicate:

```text
resource.TenantId == context.TenantId
```

The predicate is semantic IR and must survive planning and provider lowering. The caller cannot replace it with a predicate supplied in the request.

## 5. Write policy

Writes are opt-in. A role that can read an entity is not automatically allowed to mutate it. Inventory writes require an operational role in this sample.

## 6. Named operation policy

Coarse write access can be refined by a domain operation name. `inventory.reconcile` is manager-only even though a warehouse operator may perform ordinary inventory updates.

## Capability discovery

`describe_capabilities` exposes a safe description of allowed, denied and conditional capabilities. It is not a credential. The server re-evaluates the policy for every actual tool call.

## 7. Client-supplied claims

`read_entity`, `write_entity`, and `policy_probe` accept an optional, untrusted `claims` dictionary from the caller itself, separate from the server-derived `actor`/`token` identity. A fail-closed `ClientClaimsValidator` is the only path a claim can take into the policy:

- Reserved identity keys (`role`, `tenant`, `tenantId`, `actor`, `isAdmin`, `admin`, `permissions`, `capabilities`, `scopes`) are never accepted — presence alone fails the whole request closed, even if the value matches reality.
- Recognized keys (`scope`, `warehouse`, `max_rows`, `reason`, `change_ticket`, `not_after`) are validated per-key; a malformed value is rejected individually.
- Unrecognized keys are dropped individually and reported back, without blocking the rest of the call.
- Evidence (`reason`, `change_ticket`) paired with an expired `not_after` is rejected as stale.

Only the accepted claims ever reach `SupplyChainAuthorizationPolicy`, and each one can only narrow what the role already allows: `scope=read-only` self-restricts writes for that call, `warehouse=<id>` ANDs an extra resource predicate onto the tenant predicate, and `reason`/`change_ticket` add a required evidence gate on top of the existing manager-only check for `inventory.reconcile`. Nothing a claim asserts can widen access.

## MCP adversarial matrix

| Attempt                                             | Expected                                      |
| --------------------------------------------------- | --------------------------------------------- |
| Cross-tenant read                                   | Denied / conditional predicate retained       |
| Restricted field                                    | Denied                                        |
| Restricted relationship                             | Denied                                        |
| Analyst mutation                                    | Denied                                        |
| Operator `inventory.reconcile`                      | Denied                                        |
| Customer inventory write                            | Denied                                        |
| Authorized operator inventory update                | Allowed                                       |
| Claim: `role`/`tenant` injection                    | Denied — call fails closed                    |
| Claim: missing/malformed/expired reconcile evidence | Denied                                        |
| Claim: self-imposed `scope=read-only`               | Allowed — honored, restricts the call         |
| Claim: `warehouse=<id>` scoping                     | Allowed — honored, narrows the result set     |
| Claim: unrecognized key                             | Allowed — dropped individually, call proceeds |
| Claim: valid reconcile evidence                     | Allowed — honored alongside the role check    |

The client is intentionally protocol-level and small so the security demonstration does not depend on a model provider. It is an adversarial caller, not a trusted test harness.
