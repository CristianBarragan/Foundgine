# Adversarial & Security Boundary Testing

Files: `Tests/GraphSecurityBoundaryTests.cs`, `Tests/OpenIntentMutationSecurityTests.cs`,
`Tests/OpenIntentSupplyChainTests.cs`, `Tests/CapabilityBoundaryTests.cs`.

The other docs in this set cover authorization *policy* (`01`), read-side
traversal correctness (`02`), refusing to guess (`03`), and where candidates
come from (`04`). This one covers a different axis: proof that the
**mechanisms** those policies rely on — bounded traversal, graph-level
pruning, mutation authoring, and field-level result shaping — actually hold
under a caller that is open-ended or actively adversarial, not just under
the happy path the starter sample exercises.

## Why "open intent" needs its own security tests

The starter sample's MCP tools are closed: `get_my_orders`, `place_order`,
and so on are fixed shapes with fixed arguments, so there's no room for a
caller to ask for something the API doesn't already anticipate. This sample
exposes an **open intent surface** instead — `ReadIntent` and
`SemanticMutationIntentBuilder` let a caller (or an agent) describe an
arbitrary read or write shape at runtime: any entity, any relationship
traversal, any depth. That flexibility is exactly what makes agentic use
useful, and exactly what makes it a security surface: nothing stops a
caller from asking for a traversal four levels deep, or a mutation graph
with a forward-referencing dependency, unless something *inside* the
compiler and planner refuses it.

## Graph-level security: two boundaries, tested against the real domain

`GraphSecurityBoundaryTests.cs` exercises two boundaries from Foundgine's
semantic-security layer, deliberately against this sample's own generated
domain (the recursive `Product.components -> componentProduct` bill-of-
materials edge, and the `Supplier.incidents` relationship) rather than a
synthetic model — so a regression here would be caught in a shape that
looks like real usage, not just in the core library's own unit tests.

**Traversal depth.** A `ReadIntent` that walks
`Product -> components -> componentProduct -> components` is 4 levels deep.
`ReadIntentCompiler.CompileOperationGraph` takes a `SecurityResourceLimits`
and throws before planning or execution if the intent's depth exceeds
`MaxOperationGraphDepth` — the test pins this two ways: a limit of 3
rejects the intent (with a message that names depth as the cause), and the
*identical* intent compiled with a limit of 4 succeeds and produces exactly
4 graph nodes. Testing both sides matters: it proves the limit itself
caused the rejection, not some incidental property of the traversal shape.

**Graph-level authorization.** Depth limits stop a request from being too
*expensive*; they say nothing about whether the caller is *allowed* to see
every node in it. `Supplier.incidents` is denied to every role except
`Analyst` and `SupplyChainManager` (see `01-Claims-And-Authorization.md`).
The test compiles one `ReadIntent` covering `Supplier.Name` plus
`incidents.Severity`, then runs `SemanticAuthorizer.AuthorizeGraphWithEvidence`
twice against the *same compiled graph* — once as `WarehouseOperator`, once
as `Analyst`. For the operator, the `incidents` subtree (the
`ComplianceIncident` node) is pruned entirely and only the `Supplier` node
survives; for the analyst, both nodes remain. Authorization here removes a
whole relationship subtree from the graph the planner will ever see —
it isn't a filter applied to rows after the fact.

## Open-intent mutations: the authoring surface has its own fail-closed rules

`OpenIntentMutationSecurityTests.cs` and `OpenIntentSupplyChainTests.cs`
test `SemanticMutationIntentBuilder` itself — the thing a caller uses to
*describe* a write. Because the surface is open (arbitrary entities, fields,
and cross-operation dependencies), the builder has to reject malformed
authoring at build time rather than let a bad shape reach the planner:

- **Update/Delete require a target filter.** `.Update("PurchaseOrder").Set(...)`
  with no `.Where(...)` throws on `.Build()` — an unscoped update or delete
  is refused before it can become "every row," not caught later by a
  human reviewing generated SQL.
- **Upsert requires explicit conflict semantics.** `.Upsert(...)` without a
  `.Conflict(...)` clause throws; only once a conflict target is named does
  the operation build. Without this, "insert or update" would have no
  defined key to decide *which* row an existing row is a conflict with.
- **Field and entity names are resolved before planning, not at execution.**
  `.Create("PurchaseOrder").Set("SupplirId", 1)` (misspelled) and
  `.Create("PurchseOrder")` (misspelled entity) both throw immediately —
  the builder resolves every name against the semantic model as it's
  authored, so a typo fails fast and locally instead of surfacing as a
  cryptic SQL error, or worse, silently no-op-ing.
- **Dependencies cannot reference a future operation.** A `.SetFrom(...)`
  that points at an operation not yet created in the builder chain throws.
  The builder deliberately refuses to let a planner or provider invent an
  ordering later to make a forward reference work — dependency order is
  part of what the caller authors, not something inferred downstream.
- **Target filters are semantic values, not provider text.** The `Where`
  clause on an `Update` is asserted to produce a typed `SemanticFieldFilter`
  (operator + value) inside the mutation graph itself — confirming the
  filter is a structured part of the semantic intent that authorization and
  planning can inspect, not a string fragment assembled for a specific SQL
  dialect.

`OpenIntentSupplyChainTests.cs`'s fan-out case (`PurchaseOrder` ->
`PurchaseOrderLine` + `Shipment`, all sharing one generated identity) is the
positive-path companion to these: it proves the *legitimate* version of a
multi-step, dependency-ordered mutation graph plans correctly, so the
rejections above are shown to be about the specific malformed shapes, not
about open-intent mutations being restricted in general.

## `CapabilityBoundaryTests`: the field-leak boundary, pinned against this domain's own shape

The other three test files are about what a caller is *allowed to ask for*.
`CapabilityBoundaryTests.cs` is about a narrower and easy-to-miss failure:
even a fully authorized, correctly-scoped query must never let a field the
caller didn't select — or a join key that only exists to make a traversal
possible — leak into the result row.

This sample's own generated domain doesn't happen to have a
backing-only column (every CLR property is a selectable semantic field), so
these tests hand-build a small `MetadataRegistry` shaped the way a real ERP
integration often is: a `PurchaseOrder.SupplierId` foreign key that's
present on every backing row and required to resolve the
`Supplier -> PurchaseOrders` relationship, but that has no `FieldMetadata`
entry and is never exposed as a selectable field.

Two cases, both asserting the same invariant from different angles:

- **A plain leaf projection** (`Supplier.Name` only) must return exactly one
  value per row, with `EffectiveCells.Count == Values.Count` — an
  `InternalCreditModelVersion` column that exists on the backing row but was
  never selected or given field metadata must not appear anywhere in the
  result, even though the in-memory row physically carries it.
- **A traversal** (`Supplier -> PurchaseOrders`, selecting `Supplier.Name`
  and `PurchaseOrder.Status`) must expose exactly those two fields — the
  `SupplierId` join key that made the traversal possible must not leak into
  `EffectiveCells`, despite being present on the backing row and load-bearing
  for the merge itself.

The point of hand-authoring the metadata rather than relying on the sample's
generated domain: the invariant is only meaningfully tested when a
backing-only, join-only field actually exists to try to leak. A domain
where every column is already a public field can't exercise this path at
all — which is exactly why the test builds the one shape that can.

## How this doc set fits together

Read in this order, the five docs describe one continuous chain of
guarantees for an agent operating against an open, natural-language-ish
intent surface instead of a fixed API:

1. **`01`** — who is the caller, and what can their role touch at all.
2. **`02`** — do the read-side business scenarios stay correct and bounded
   even against adversarial data (cycles, unbounded depth).
3. **`03`** — when a phrase is ambiguous or unrecognized, refuse instead of
   guessing.
4. **`04`** — where the candidate interpretations that feed grounding come
   from, and how each retrieval strategy degrades when its backing
   capability isn't available.
5. **`05`** (this doc) — even once a request is authorized, bounded, and
   grounded, the compiler, authorizer, and execution boundary each still
   enforce their own limits: traversal depth, subtree-level denial,
   fail-closed mutation authoring, and never leaking an unselected or
   join-only field.

Every layer fails closed on its own terms. None of them assumes an earlier
layer already caught the problem.
