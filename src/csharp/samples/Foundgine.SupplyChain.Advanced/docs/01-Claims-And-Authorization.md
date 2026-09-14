# Claims and Authorization

Files: `Authorization/SupplyChainAuthorization.cs`, `Authorization/ClientClaims.cs`,
`Authorization/Claims/*.cs`.

## The concept: identity vs. claims

The starter sample's authorization (`Foundgine.SupplyChain/Application/Authorization.cs`)
only has one kind of input: an `actor` + `token` pair, resolved server-side
into a fixed identity. This sample adds a second, deliberately weaker kind of
input: **claims** — extra context the *caller* asserts on top of that
identity (`scope=read-only`, `warehouse=12`, `reason=...`, `change_ticket=...`).

The single rule that makes this safe, stated in `ClientClaimsValidator`'s own
doc comment and worth repeating because everything else follows from it:

> **Identity** (tenant, role) is never taken from the caller — it is resolved
> server-side from the actor/token pair. **Claims** are additional,
> caller-asserted context that can only ever *narrow* what the policy already
> allows for the authenticated role. Claims are never additive to privilege.

## Why claims need their own validation pipeline

Because an MCP client can send arbitrary JSON, `ClientClaimsValidator.Validate`
treats every claim as hostile until proven otherwise, in this order:

1. **Reserved-identity-key check first, and it's a whole-request failure.**
   `IdentitySpoofingValidator` checks the raw claim keys against
   `ClaimSchema.ReservedIdentityKeys` (`role`, `tenant`, `actor`, `isadmin`,
   `permissions`, …) *before* anything else runs. If any reserved key is
   present at all — even with a value that happens to match the caller's
   real identity — the **entire request** is rejected, not just that one
   claim. The doc comment on `IdentitySpoofingValidator` explains why:
   partially processing the rest of the call would still leak information
   about which other claims *would* have been honored, and "a client that
   tries this once has demonstrated intent that should not be trusted with
   partial processing."
   - `HostileReservedIdentityKeys` (a subset: `isadmin`, `permissions`,
     `capabilities`, `scopes`) is classified `Hostile`; the rest of the
     reserved set is `Suspicious`. Both are rejected identically — the
     severity only exists so operators can triage differently, not to
     relax the fail-closed behavior.
2. **Per-key format validation.** Every *recognized*, non-identity key
   (`scope`, `warehouse`, `max_rows`, `reason`, `change_ticket`, `not_after`)
   has its own `IClaimValidator` registered on `SupplyChainClaimSchema.Default`
   — e.g. `warehouse` must parse as a positive integer, `change_ticket` must
   match `^CHG-\d{4,}$`. A malformed value is rejected *individually*; it
   doesn't fail the whole request, but any privilege that depended on it is
   evaluated as if the claim were absent.
3. **Unrecognized keys are dropped, not rejected wholesale.** Fail-closed on
   trust (an unrecognized key never grants anything), fail-open on noise (one
   unrecognized key doesn't sink the request) — the rejection is still
   reported back so a legitimate caller can see what was ignored.
4. **Cross-field / expiry validation.** `CrossFieldClaimValidator` enforces
   that evidence-bearing claims (`reason`, `change_ticket`) are only honored
   alongside a `not_after` expiry that (a) hasn't already passed and (b)
   doesn't exceed `ClaimSchema.MaxExpiryHorizon` (7 days, in
   `SupplyChainClaimSchema`). The horizon ceiling matters specifically
   because without it, a caller could hand-write an expiry decades out and
   have the evidence trusted as if it never expired.

## Why a `ClaimSchema` object instead of static fields

`ClaimSchema` bundles the reserved-key set, the hostile subset, the per-key
validators, and the expiry rules into one instance
(`SupplyChainClaimSchema.Default`) rather than hard-coding them as static
fields on the validator itself. The stated reason (`ClaimSchema`'s own doc
comment): a different vertical, tenant, or schema version can build its own
instance and reuse `IdentitySpoofingValidator` / `CrossFieldClaimValidator` /
`ClientClaimsValidator` completely unchanged — those three are generic over
*any* `ClaimSchema`, and this file is the only place SupplyChain-specific
claim rules live.

## The authorization policy itself: five rule kinds, not one

`SupplyChainAuthorization.Create(tenantId, role, claims)` builds a
`SemanticAuthorizationConfiguration` from five independent rule callbacks.
Each answers a narrower question than "is this allowed," and Foundgine's
authorizer combines them — this sample doesn't reimplement that combination
logic, only supplies the SupplyChain-specific answers:

| Rule | Question it answers | Example from this sample |
|---|---|---|
| Entity rule | Can this role touch this entity at all? | `Customer` role can never read `Supplier`, `Certification`, `ComplianceIncident`. |
| Field rule | Can this role see/write this *specific field*? | `InventoryLot.Quarantined` is readable only by `WarehouseOperator`/`SupplyChainManager`; `Supplier.RiskScore` only by `Analyst`/`SupplyChainManager` — see `SensitiveFieldAuthorizationTests.cs` for the exhaustive matrix. |
| Relationship rule | Can this role traverse this edge? | `Supplier.incidents` is only traversable by `Analyst`/`SupplyChainManager` — a `Customer` querying `Supplier` never even sees that edge exists. |
| Predicate rule | What *row-level* filter applies even when the entity/field/relationship checks pass? | Every `Supplier`/`Warehouse` read gets a `TenantId == context.TenantId` predicate; if the caller's `warehouse` claim was accepted, an additional `WarehouseId == <claimed warehouse>` predicate is **AND**ed on top. |
| Named-operation rule | Is this specific *named write operation* (not just "any write") allowed? | `"inventory.reconcile"` requires the `SupplyChainManager` role **and** both a `reason` and `change_ticket` claim to have survived validation — an ordinary `"update"` on the same entity does not require either. |

`AuthorizationPolicyTests.cs` exercises all five in one test to show they're
genuinely independent axes, not layers of the same check — an
`Analyst` can access `ComplianceIncident` (entity ✅) but not
`InventoryLot.Quarantined` (field ❌); a `WarehouseOperator` can perform
`update` but not `inventory.reconcile` on the same entity (named-operation ❌
while the general write rule would say ✅).

## Why claims narrow predicates instead of replacing them

Look at `GetPredicate`: the tenant predicate is always applied for
`Supplier`/`Warehouse` reads, *and then*, only if the caller supplied a valid
`warehouse` claim, a second predicate is ANDed on top narrowing to that one
warehouse. There's no path where a claim can *replace* the tenant predicate
or grant access to a warehouse outside the tenant — claims can only add
`AND`ed restrictions, never `OR`ed exceptions. That asymmetry is what makes
"claims never add privilege" actually true in the implementation, not just
true in the doc comment.

## A note on how relationship/field ids are resolved here — and why

`SupplyChainAuthorization.FieldIds`/`RelationshipIds` resolve ids by
**string name** at static-initialization time, e.g.:

```csharp
public static RelationshipId SupplierCertifications =>
    SupplyChainSemanticModel.Relationship("Supplier", "certifications");
```

If you read the walkthrough for the simpler `Foundgine.SupplyChain` starter,
you'll recognize this as the exact pattern that starter's semantic-model
wrapper *used to* have — and which we replaced there with compiler-checked
constants the AOT generator now emits directly
(`GeneratedSemanticModel.<Model>.Relationships.<Name>`).

**We deliberately did not make that same change here**, for a reason this
sample's own `Infrastructure/README.md` states explicitly: *"there is
deliberately no hand-maintained structural model... replacing the AOT
producer with an EF/database/other metadata producer should not require
changes to semantic configuration."* `SupplyChainSemanticModel.Build()` also
layers in **logical traversals** that don't exist in generated metadata at
all (`Product.shipments`, `Product.supplierIncidents` — see
`SupplyChainSemanticModel.cs`'s `Build()` method) alongside the structural
relationships. Any single lookup mechanism used everywhere in this file has
to handle both kinds uniformly, and only the runtime, name-based
`Model.Get(...).Relationships.Single(...)` lookup can — a
compile-time-generated constant only exists for the structural
relationships, not the synthesized traversals.

The typo-safety the compile-time constants would have bought you is instead
covered here by the test suite: `SemanticModelTests.cs` and
`MetadataProducerBoundaryTests.cs` resolve every id this file uses, so a
renamed or misspelled relationship fails a fast, deterministic unit test —
not a `Single()` throw discovered in production. If you're extending this
project and specifically want compile-time-checked ids for a *structural*
(non-traversal) relationship, `GeneratedSemanticModel.<Model>.Relationships.<Name>`
is available after a rebuild — just know that using it here is a deliberate
trade against the "swap metadata producers freely" design goal, not a
strict improvement.

---
Next: [`02-High-Assurance-Scenarios.md`](./02-High-Assurance-Scenarios.md)
