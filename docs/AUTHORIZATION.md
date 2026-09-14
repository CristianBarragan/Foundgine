# Authorization

Foundgine treats authorization as part of semantic execution rather than as a
transport-specific check around execution.

The model has four important boundaries:

![PlantUML diagram: AUTHORIZATION, diagram 1](assets/authorization-plantuml-01.svg)

A policy can therefore describe a domain such as:

![PlantUML diagram: AUTHORIZATION, diagram 2](assets/authorization-plantuml-02.svg)

## Authorization in the canonical lifecycle

Authorization is stage 7 of the canonical lifecycle: **Caller → Intent → Semantic Model → Semantic Operation Graph → Retrieval → Resolution → Authorization → Plan Binding → Execution IR → Provider → Execution → Evidence**. Retrieval can discover candidates, but authorization evaluates the resolved operation graph under the trusted semantic contract.

![PlantUML diagram: AUTHORIZATION, diagram 3](assets/authorization-plantuml-03.svg)

A retrieval result, capability description, or caller-supplied claim never becomes authority merely because it helped construct intent.

## Semantic operation graph and authorization provenance

Authorization applies to the complete resolved semantic operation graph. The graph is validated before policy evaluation and the resulting decision is captured as immutable evidence.

![PlantUML diagram: AUTHORIZATION, diagram 4](assets/authorization-plantuml-04.svg)

The plan binding contains two identities:

- the semantic contract fingerprint;
- the authorization-decision fingerprint.

This is provenance, not a reusable permission token. A plan cannot be detached from the contract or authorization evidence that produced it. Rewrites must preserve the binding, and execution rejects mismatches.

The final provider execution gate adds a second proof: the provider artifact must be security-conformant for the exact `ExecutionIR` being executed.

## Conditional authorization

Conditional access is represented by the small provider-independent predicate
IR in `Foundgine.Core.Abstractions`.

For example:

```csharp
Expression<Func<UserContext, Employee, bool>> CanReadEmployee =>
    (user, employee) => user.TenantId == employee.TenantId;
```

The generator/policy layer can represent the condition as:

![PlantUML diagram: AUTHORIZATION, diagram 5](assets/authorization-plantuml-05.svg)

The expression tree is not retained and is never compiled or invoked by the
runtime. The predicate travels with the semantic graph into the
provider-independent execution plan.

Providers lower that predicate into their native representation. The SQL
provider, for example, turns the resource member into a storage column and
binds the context member as a runtime parameter.

## Capability discovery

Foundgine also exposes a provider-independent capability description through
`DescribeCapabilities()`.

This is intended for callers that need to understand what they can ask for,
including AI agents:

![PlantUML diagram: AUTHORIZATION, diagram 6](assets/authorization-plantuml-06.svg)

Capability discovery is **descriptive, not authoritative**. An agent or API
caller must never be trusted because it previously received a capability
snapshot. The execution pipeline evaluates authorization again before a plan
is produced.

The capability model reports `Denied`, `Allowed`, or `Conditional` for entity,
field, and relationship access. Policy implementation details are not exposed
as a requirement for the caller.

## Write authorization

Write access is explicitly opt-in. Existing read-only policies do not
accidentally become write-enabled.

Mutation authorization checks:

- entity write permission;
- field write permission for supplied values;
- field read permission for returned fields;
- read permission for fields and relationships used by mutation filters.

`MutationPlanner` remains structural. `MutationAuthorizer` applies the
semantic policy after planning and before provider compilation.

## Caching boundary

Authorization predicates must remain part of execution semantics.

A provider plan cache may safely reuse an already-authorized plan shape, but it must not turn:

```text
resource.TenantId == context.TenantId
```

into an authorization-free cached plan.

The intended model is:

![PlantUML diagram: AUTHORIZATION, diagram 7](assets/authorization-plantuml-07.svg)

Claims, roles, identity providers, and policy administration are deliberately
outside this layer. They can sit above Foundgine and produce semantic policy
decisions without becoming part of the Foundgine core.

## SupplyChain reference matrix

The `Foundgine.SupplyChain.Advanced` sample is the canonical worked example for the policy boundaries above. It deliberately mixes manual and generated semantic authoring and then applies a single provider-independent policy model to the resulting `SemanticModel`.

It demonstrates:

- entity allow/deny;
- field allow/deny;
- relationship allow/deny;
- conditional tenant predicates;
- opt-in write authorization;
- named-operation refinement;
- capability discovery as non-authoritative metadata; and
- MCP adversarial calls that attempt to cross those boundaries.

The sample also separates transport authentication from semantic authorization: the MCP server resolves a fixed actor/token into a tenant and role, then constructs the semantic policy for that actor. A caller cannot promote itself by supplying a different role in the semantic request.

---

Next: [Security](SECURITY.md)
