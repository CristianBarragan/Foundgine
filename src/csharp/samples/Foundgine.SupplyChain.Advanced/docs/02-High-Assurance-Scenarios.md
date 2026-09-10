# High-Assurance Scenarios

Files: `Application/Scenarios/Scenarios.cs`, `Application/SupplyChainExecutionLimits.cs`,
`Tests/RecursiveSupplierRiskTests.cs`, `Tests/FulfillmentPlanningTests.cs`,
`Tests/AdversarialInvariantTests.cs`.

The starter sample's "high assurance" story was about a single mutation
(`place_order`) getting idempotency and inventory checks. This sample's
version of high assurance is different in shape: two **read-side traversal
scenarios** that have to stay correct — and terminate — even against
adversarial or malformed data, plus authorization scoping applied *inside*
application logic, not just at the API boundary.

## Scenario 1: recursive supplier risk (`RecursiveSupplierRisk`)

**The question:** for a given product, walk its full bill-of-materials
(BOM) — components, sub-components, sub-sub-components — and report every
supplier reachable at each depth, scoped to the caller's own tenant.

**The two failure modes this has to survive, by construction, not by luck:**

1. **Unbounded depth.** A real BOM can be deep, and a malicious or malformed
   one could be *deliberately* deep to cause a stack overflow or a hung
   request. `Walk` checks `depth > SupplyChainExecutionLimits.RecursiveBomMaxDepth`
   (5) before doing anything else on each call — the traversal is bounded by
   policy, not by "however deep the data happens to go."
2. **Cycles.** A component graph is supposed to be a DAG, but nothing
   stops bad data from making product A a component of product B which is a
   component of product A. `Walk` tracks the current call-stack path in a
   `HashSet<ProductId> path` (distinct from `visited`, which is the
   whole-traversal memoization set) — `path.Add(product)` returning `false`
   means *this specific product is already an ancestor of itself in the
   current recursion*, which means a cycle, and the function records it
   (`CycleDetected: true`) and returns instead of recursing forever.

**The authorization scoping that isn't just a filter bolted on top:** notice
the supplier-collection line —
`d.Suppliers.FirstOrDefault(s => s.Id == supplier)?.TenantId == auth.TenantId`
— tenant scoping happens *inside* the traversal, at the point where a
supplier is about to be added to the result, not as a post-hoc `.Where()` on
the final list. This matters because the traversal itself (which products
lead to which components) is still allowed to walk through nodes the caller
can't see suppliers for — only the *supplier attribution* is filtered. A
looser design that filtered the whole result set after the fact would have
been simpler to write and would have produced the same output here, but
would not generalize safely to a scenario where the traversal path itself
(not just the leaf) needed to stay hidden.

## Scenario 2: fulfillment planning (`FulfillmentPlanning`)

**The question:** across all open customer orders, which products are
projected to fall short, after netting available inventory *and* inbound
shipments expected within 14 days?

**Where authorization scoping shows up again:** both the "available
inventory" sum and the "inbound shipments" sum are filtered by
`auth.AllowedWarehouses.Contains(...)` *before* being summed — a caller
scoped to one warehouse gets a fulfillment picture computed only from data
they're allowed to see, not a globally-accurate number with some fields
redacted afterward. This is the same principle as scenario 1: authorization
constrains what's aggregated, not just what's displayed.

**Business logic worth noting on its own:** available quantity is
`Math.Max(0, OnHand - Reserved - Quarantined)` — quarantined stock never
counts as available, and the `Math.Max(0, ...)` guards against a
data-quality bug (over-reservation) silently turning into "negative
inventory offsets a shortage elsewhere," which would understate real risk.

## `SupplyChainExecutionLimits`: policy, not metadata

```csharp
public static class SupplyChainExecutionLimits
{
    public const int RecursiveBomMaxDepth = 5;
    public const int MaximumPageSize = 50;
    public const int MaximumTraversalNodes = 10000;
}
```

The doc comment on this class is worth internalizing as a design principle
on its own: these are **operational/security policy**, explicitly *not*
structural metadata and *not* generated semantic topology. A schema change
(adding a field, renaming a table) should never have to touch this file, and
this file should never have to know anything about column names. Keeping
"how deep is too deep" separate from "what does the schema look like" is
what lets you tighten these limits later (say, after an incident) without
touching a single `[Foundgine*]` attribute.

## `AssertAdversarialInvariants`: executable proof, not just narrative tests

`SupplyChainScenarios.AssertAdversarialInvariants` is unusual: it's
production code (not a test file) whose entire job is to assert that the
*seed fixture itself* has certain adversarial properties, and throw if it
doesn't:

- A cross-tenant warehouse (id 3) is present in the fixture but excluded
  when the authorization context is correctly scoped.
- A BOM cycle exists in the fixture and is actually detected (not just
  "the code has a cycle-detection branch that happens to never execute").
- An expired supplier certification exists in the fixture.
- A deliberately over-broad authorization context (one that leaks the
  cross-tenant warehouse) causes this function to throw.

`AdversarialInvariantTests.cs` then wraps this in two `[Fact]`s: one proving
it passes clean for a correctly-scoped context, one proving it throws for a
leaky one. The value of this split — invariant-checking logic living in
application code, exercised by a thin test — is that anyone can run
`SupplyChainScenarios.AssertAdversarialInvariants(data, auth)` against a
*different* data fixture or a *different* auth context (e.g. in a demo, or
while debugging a production incident) and get the same fail-loud guarantee
that xUnit gives at test time.

---
Previous: [`01-Claims-And-Authorization.md`](./01-Claims-And-Authorization.md) · Next: [`03-Ambiguity-And-Grounding.md`](./03-Ambiguity-And-Grounding.md)
