# Security

Foundgine treats external intent as untrusted input.

The fundamental rule is:

![PlantUML diagram: SECURITY, diagram 1](assets/security-plantuml-01.svg)

No transport adapter should bypass the semantic/security boundary.


## Canonical security boundary

Foundgine's security story follows one lifecycle rather than separate transport-specific paths:

![PlantUML diagram: SECURITY, diagram 2](assets/security-plantuml-02.svg)

**Retrieval is never authorization.** Fuzzy, full-text, BM25 and Apache AGE graph retrieval may return candidates and evidence, but every candidate is still resolved and authorized before execution.

## Authority and intent are different

The caller controls intent:

```text
"read Customer.Name"
```

The trusted host controls authority:

```text
identity
tenant
audience
warrant
```

A request must never be able to promote itself by changing ordinary intent fields.

## Semantic authorization

Authorization is evaluated at the semantic boundary for:

- entity read/write access;
- field read/write access;
- relationship read/write access;
- conditional resource predicates;
- mutation operations and returned fields.

Capability discovery is advisory. The policy is evaluated again for the actual request.

## Conditional authorization

A policy can carry a provider-independent predicate such as:

```text
resource.TenantId == context.TenantId
```

The predicate survives into the logical plan and is lowered by the provider.

This is important for:

- tenant isolation;
- user/resource ownership;
- row-level access;
- safe plan caching with authorization-preserving context isolation.

A provider must not discard a predicate merely because the provider can produce a syntactically valid query without it.

## Resource and complexity limits

Security is not only about authorization. A caller can also attack the semantic
engine with structurally expensive intent. `SecurityResourceLimits` is the
canonical engine-side guard and applies independently of whether the request
arrived through JSON, MCP, GraphQL, C#, or another adapter.

The default bounds include:

| Resource | Default maximum |
|---|---:|
| Selection depth | 32 |
| Selection nodes | 256 |
| Operation-graph nodes | 256 |
| Operation-graph depth | 32 |
| Operation-graph fields | 512 |
| Filter depth | 32 |
| Filter nodes | 256 |
| Order terms | 64 |
| Order-path depth | 16 |
| Page size | 1,000 |
| Offset | 1,000,000 |
| Cursor length | 4,096 |

Mutation requests also have independent bounds for operations, fields, return
fields, dependencies, and effects. Applications can tighten the defaults for
their threat model; the important invariant is that untrusted request complexity
is bounded before it can consume unbounded planner/provider resources.

## Plan-cache boundary

Provider plan caching is an optimization boundary, not an authorization boundary.
A cache entry is derived from the complete provider-independent plan, including
its authorization semantics. Runtime context values are supplied at execution
time rather than becoming an alternate source of authority.

The safe lifecycle is:

![PlantUML diagram: SECURITY, diagram 3](assets/security-plantuml-03.svg)

A cache hit must never skip semantic resolution or authorization, and provider
conformance must still be satisfied before execution.

## Logical traversals

A logical traversal can hide intermediate entities:

![PlantUML diagram: SECURITY, diagram 4](assets/security-plantuml-04.svg)

Authorization sees the expanded path.

Therefore:

```text
deny Contract
    ⇒ deny Customer.transactions
```

A traversal is not a security shortcut.

## Capability discovery

Capability discovery exists so dynamic callers and AI agents can construct valid intent.

![PlantUML diagram: SECURITY, diagram 5](assets/security-plantuml-05.svg)

A capability document is not an authorization token.

## Fail-closed projection

An especially important invariant is that an empty authorized field set must not mean "select everything".

The execution/provider boundary must reject an invalid empty projection rather than widening the selection.

## Provider security conformance

A provider can execute valid SQL and still violate Foundgine's security contract.

Foundgine therefore carries required security invariants into execution and checks provider conformance.

![PlantUML diagram: SECURITY, diagram 6](assets/security-plantuml-06.svg)

## GraphQL

GraphQL input is untrusted.

Do not accept identity, tenant, audience, warrant, or provider control information from GraphQL variables/arguments as trusted authority.

Use:

`Foundgine.Extensions.GraphQL.HotChocolate.HotChocolate.Execution`

for the secure query execution path.

For mutations use:

`Foundgine.Extensions.GraphQL.HotChocolate.HotChocolate.Mutations`.

## MCP

MCP tool arguments are untrusted.

The MCP host should obtain security context from authenticated session/request state.

![PlantUML diagram: SECURITY, diagram 7](assets/security-plantuml-07.svg)

Do not allow an agent to select its own tenant or authorization role.

## AI

An LLM is an untrusted producer of intent.

Avoid:

![PlantUML diagram: SECURITY, diagram 8](assets/security-plantuml-08.svg)

Use:

![PlantUML diagram: SECURITY, diagram 9](assets/security-plantuml-09.svg)

The application remains responsible for authentication, model credentials, rate limits, quotas, and prompt/application policy.

## JSON

JSON intent should be bounded with `JsonReadIntentAdapterOptions`.

Structural limits protect the parser/intent boundary, while semantic authorization protects the operation.

## Mutations

Writes have stronger security requirements than reads.

The mutation path can include:

![PlantUML diagram: SECURITY, diagram 10](assets/security-plantuml-10.svg)

A mutation builder is an authoring tool, not an authorization mechanism.

## Plan caching

Caching must never cache away authorization.

The safe conceptual model is:

![PlantUML diagram: SECURITY, diagram 11](assets/security-plantuml-11.svg)

## Authority recovery

`Foundgine.Runtime.ControlPlane` is optional.

It provides authority/control-plane recovery primitives such as witness quorum, credential lifecycle, journal reconciliation, promotion/failover, and recovery evidence.

It is not required for normal semantic authorization and remains outside the core execution path.

## Application responsibilities

Foundgine does not replace application security infrastructure.

The application/host remains responsible for:

- authentication;
- identity lifecycle;
- tenant resolution;
- secret management;
- network security;
- rate limits;
- quotas;
- endpoint/session policy;
- external identity provider integration;
- authorization policy administration.

Foundgine enforces the semantic execution boundary supplied by the application.

## Security testing rule

Every new transport/provider should have adversarial tests proving that it cannot:

1. bypass semantic resolution;
2. bypass authorization;
3. replace host-owned authority;
4. drop a required security predicate;
5. execute a plan without required provider security conformance.

## Red-team pentest results (Advanced Supply Chain sample)

This section records the outcome of a live adversarial run of `Foundgine.RedTeam`
against the running `Foundgine.SupplyChain.Advanced` sample, exercising both the
semantic authorization API (port `4432`) and the execution/tool-calling API
(port `4422`) over MCP's Streamable HTTP transport.

### Run summary

After both fixes, 18 attack attempts were sent across the two surfaces. All reached
the MCP endpoint (HTTP 200 at the transport layer) and were evaluated by the
JSON-RPC/authorization layer underneath:

| Surface | Target | Requests | Legitimate baseline succeeded | Adversarial attempts blocked |
|---|---|---:|---:|---:|
| Semantic authorization API | `http://127.0.0.1:4432/` | 11 | 1/1 | 10/10 |
| Execution API | `http://127.0.0.1:4422/` | 7 | 1/1 | 5/5 (1 unauthenticated baseline also blocked) |

### Attacks attempted and outcome

**Semantic authorization API**

| Attack | Result |
|---|---|
| Authenticated capability discovery (baseline) | Succeeded — capability document returned, all entity/field access correctly marked `Denied` where policy disallows it |
| Cross-tenant policy probe | Denied |
| Named operation escalation | Denied |
| Identity claim spoofing | Rejected — server error explicitly states claims cannot assert identity/privilege directly; identity comes only from actor/token authentication |
| Entity write escalation | Denied |
| Wrong-token authentication bypass | Failed before reaching the tool (auth rejected) |
| Relationship authorization escalation | Denied |
| Sensitive field authorization probe | Denied |
| Unknown actor authentication | Failed before reaching the tool (auth rejected) |
| Claim boundary manipulation (attempted `scope: *` widening) | Denied — the widened claim was explicitly rejected, not silently narrowed |

**Execution API**

| Attack | Result |
|---|---|
| Authenticated execution baseline | Succeeded — legitimate product lookup returned data |
| Cross-customer order access | Blocked — tool invocation errored rather than returning another customer's order |
| Customer → warehouse capability escalation (`update_inventory`) | Blocked |
| Customer shipment write escalation (`create_shipment`) | Blocked |
| Customer supplier enumeration (`list_suppliers`) | Blocked |
| Unknown actor authorization (`update_inventory`) | Blocked |

### Assessment

Across both surfaces, only the two intentionally-legitimate baseline calls returned
data; every cross-tenant, role-spoofing, claim-widening, and unauthenticated-actor
attempt was denied or errored out before reaching the underlying operation. This is
consistent with the security invariants described earlier in this document —
in particular that retrieval/capability discovery is advisory only, that claims
cannot self-assert identity or privilege, and that authorization is re-evaluated
for the actual request rather than trusted from discovery.

### Why the two APIs' error shapes differ

The execution API's uniform `"An error occurred invoking '<tool>'."` message is not
an accident of this sample — it is the MCP C# SDK's own default behavior. The SDK
sanitizes tool-invocation failures by design: any exception other than its own
`McpException` type is collapsed into that generic message before it reaches the
caller, precisely so a server author cannot accidentally leak exception details
(stack traces, internal identifiers, database errors) to an untrusted MCP client.
`McpException.Message`, by contrast, is documented as safe to propagate verbatim —
it exists for a tool to deliberately choose what to tell the caller.

The semantic API's richer response (`{"result":{"allowed":false,"kind":"Denied"}}`,
and the named-claim detail on spoofing attempts) is not a framework default at all.
It comes from `Semantic/Api/Mcp/Program.cs`'s `policy_probe` tool, which is a
teaching/lab tool: its explicit purpose is to let a caller see which authorization
decision path was taken (`Denied` vs. a `conditional` predicate vs. an outright
error), so the Supply Chain sample can demonstrate the security model described
earlier in this document. That is a reasonable choice for a documentation/lab
surface whose whole job is to make policy decisions legible — it is not necessarily
the right choice for the same shape on a production authorization endpoint, where a
caller probing many variants could use `Denied` vs. `RequiresClarification` vs. the
specific rejected-claim detail to map out policy boundaries faster than a uniform
error would allow.

### Implemented: server-side classification with a correlation id

The execution API's `Execute` helper in
[`Semantic/Api/Mcp/Program.cs`](../samples/Foundgine.SupplyChain.Advanced/Semantic/Api/Mcp/Program.cs)
now:

1. Generates a short opaque correlation id for every call before it runs.
2. Classifies any failure — authorization denial, not-found, validation error,
   invalid-operation, or an unhandled exception — into a stable, greppable
   `BlockedCallClassification`, independent of which .NET exception type happened
   to be thrown for it.
3. Logs the classification, correlation id, capability, and actor together with the
   full exception via `ILogger`, so an operator can distinguish "blocked by
   authorization" from "blocked by an unrelated bug" from the server-side log alone.
4. Throws a single `McpException` whose message is uniform across every
   classification — `"Request blocked while invoking '<tool>'. Reference:
   <correlationId>."` — so the correlation id reaches the caller (via the one
   exception type the SDK propagates verbatim) without the message itself ever
   varying by cause. The id lets a support ticket or alert be joined back to the
   exact log line; it carries no information an attacker could use as an oracle.

This keeps the caller-facing surface exactly as uniform as the SDK's own default —
it does not make the execution API's errors richer, and it does not adopt the
semantic API's shape.

### Open question: the semantic API's response verbosity

The semantic API's `kind: Denied` / `RequiresClarification` distinction and its
named-claim detail on spoofing attempts were not changed. They are appropriate for
a lab tool whose purpose is demonstrating the authorization model, but the same
question above applies to it as much as it did to the execution API: is that detail
something a live caller should see, or does it belong in server-side logging only
(the same correlation-id pattern used above would apply directly)? Recommended
follow-up is to make that call explicitly — either document `policy_probe` as a
lab-only surface not meant to be exposed as-is, or apply the same
classification/correlation-id treatment to it before any production use.

---

Next: [Runtime](RUNTIME.md)
