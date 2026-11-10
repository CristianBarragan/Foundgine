# Extensions (GraphQL, Keycloak, Temporal)

> **In plain words.** An **extension** plugs a popular tool into Foundgine so you do not have to write the glue yourself. Foundgine has three: **GraphQL** (so a GraphQL API can talk to Foundgine), **Keycloak** (so people who log in with Keycloak get the right permissions) and **Temporal** (so long-running business processes, like "onboard a customer", can run reliably over days). Extensions are **optional** and only *translate*. They never make permission decisions on their own: every request still goes through Foundgine's normal checks.
>
> *Extension or provider?* A **provider** does the real work against storage or infrastructure (SQL, EF Core, JPA, Elasticsearch...), see the [Provider catalogue](providers). An **extension** connects an outside *framework or service* to Foundgine. Some extensions also register themselves as providers so Foundgine can see them (Keycloak as an `Identity` provider, Temporal as a `Workflow` provider).

This is the **only** page for integrations. The older "Integrations" overview repeated this content and now just points here.

Extensions adapt a framework, identity system or workflow engine to Foundgine's contracts. They are optional: install only the integrations you use. An extension is an adapter, not a second semantic engine: it translates external requests or context into Foundgine contracts and delegates semantic resolution, authorization and execution to the core/runtime boundary.

## Every extension by name

| # | Extension | What it is, in plain words | C# source | Java source |
|---|---|---|---|---|
| 1 | **GraphQL, Hot Chocolate adapter** | Reads a GraphQL query and turns it into a Foundgine request | `src/csharp/Foundgine.Extensions/GraphQL/HotChocolate` | `src/java/foundgine-extensions/.../graphql` |
| 2 | **GraphQL secure execution** (`...HotChocolate.Execution`) | Runs those requests through Foundgine's safe path (permissions, plan, provider) | `GraphQL/HotChocolate/Execution` | query and mutation executors in `...graphql` |
| 3 | **Keycloak identity** | Turns a Keycloak login into trusted Foundgine identity and short-lived signed permission slips (warrants) | `src/csharp/Foundgine.Extensions/Keycloak` | `src/java/foundgine-extensions/.../keycloak` |
| 4 | **Temporal durable workflows** | Runs long, multi-step business processes with retries, timers, approvals and undo steps | `src/csharp/Foundgine.Extensions/Temporal` | `src/java/foundgine-extensions/.../temporal` |
| 5 | **Extension registry** | A small list of which extensions are installed | `src/csharp/Foundgine.Extensions` | `com.foundgine.extensions` |

> Both language modules contain GraphQL, Keycloak and Temporal source trees in this repository snapshot. That does not establish exact API, feature or maturity parity. Check each language's implementation and tests before assuming equivalent behaviour.

**Install what you use.** In .NET the umbrella package is `Foundgine.Extensions`. It does **not** include the Foundgine runtime, any storage provider, an MCP server or an LLM client.

## Which extension do I need?

| If you... | Use |
|---|---|
| Expose a GraphQL API | GraphQL (adapter + secure execution) |
| Log users in with Keycloak and want them to get Foundgine permissions | Keycloak |
| Need a process that waits, retries, asks for approval or can be undone | Temporal (with Keycloak so workers get proper permissions) |
| Need a database or search backend | Not an extension: pick a [provider](providers) |
| Want AI agents to call Foundgine | Not an extension: see [AI agents and MCP](agents-mcp) |

## 1 and 2. GraphQL integration

**Purpose.** Connects a GraphQL API to Foundgine's semantic execution boundary. GraphQL is the external transport/schema contract; Foundgine remains responsible for semantic interpretation, authorization, planning and provider execution.

**C# components**
- `GraphQLSchemaAdapter` and `GraphQLSchemaDescriptor`: schema description/bridging.
- `HotChocolateSemanticAdapter`: converts GraphQL selections into semantic requests.
- `GraphQLVariableCoercer`: coerces variables and values at the transport boundary.
- `GraphQLDirectiveEvaluator`: evaluates supported directives.
- `GraphQLResultShape`: describes result shaping.
- Mutation adapter/converter/result shaper: adapts GraphQL mutation input and Foundgine mutation results.
- Query/mutation executors: invoke the secure Foundgine execution boundary.

The C# source is under `GraphQL/HotChocolate`; query/mutation execution has a dedicated `Execution` folder. Some APIs are split into package-level adapters and execution adapters, so inspect the package README and project/package references before installing only part of the integration.

**Java components.** The `foundgine-extensions` module contains GraphQL schema adaptation, type mapping, directive evaluation, variable coercion, query and mutation semantic conversion, result shaping, error types, and query/mutation executors under `com.foundgine.extensions.graphql`.

**Request path**
1. GraphQL parser/schema layer validates the transport request.
2. Adapter maps fields, arguments, variables and supported directives into Foundgine semantic intent.
3. Foundgine runtime resolves, authorizes and plans the operation.
4. The configured provider executes the authorized plan.
5. The adapter shapes the result or maps a safe adapter error.

**Security requirements**
- GraphQL schema visibility and resolver-level checks are not substitutes for Foundgine execution-time authorization.
- Do not build a parallel direct database path inside GraphQL resolvers.
- Bind tenant/subject context from trusted authentication middleware, never from arbitrary GraphQL arguments.
- Apply request depth/complexity, pagination, timeout and cancellation limits at the GraphQL host as well as Foundgine's own execution budgets.
- Mutation dry-run, approval and execution must preserve the runtime's fingerprint/authority checks.

**Source:** `src/csharp/Foundgine.Extensions/GraphQL/HotChocolate` and `src/java/foundgine-extensions/src/main/java/com/foundgine/extensions/graphql`.

## 3. Keycloak identity integration

**Purpose.** Bridges Keycloak authentication and identity claims into Foundgine's own security execution context and signed warrant model. Keycloak authenticates the principal; Foundgine still decides whether a particular semantic operation is authorized.

**C# components**
- Authentication/JWT validation and key resolution.
- Claim mapping for subject, tenant and roles.
- `KeycloakSecurityExecutionContextProvider`.
- `FoundgineWarrantIssuer` and `FoundgineWarrantKeyResolver`.
- Service-token provider for client-credentials flows.
- Keycloak admin client/operations for supported administrative workflows.
- Temporal security-context factory for worker activity execution.
- Hosting/DI registration and options.

**Java components.** The Java extension module contains JWT validation/JWKS key sourcing, bearer authorization, claim/principal mapping, Keycloak options/grants, warrant issuer and signing-key provider, warrant key resolver, service-token provider, execution-context provider, hosting provider and admin client/operations.

**Trust flow**
1. Validate token issuer, audience, signature, lifetime and key rotation using a trusted JWKS/key source.
2. Derive subject, tenant and role/grant information only from validated claims.
3. Map identity to a Foundgine security execution context and issue/resolve a short-lived warrant according to the host's configured policy.
4. Runtime validates the warrant, scope, constraints and replay protection before executing each operation.

**Production guidance**
- Never trust tenant, role or subject values supplied in request bodies or unverified headers.
- Use a mounted secret or KMS/HSM-backed signing-key provider; ephemeral keys are development-only.
- Use distinct key IDs/keys for separate hosts or workers and distribute only trusted public keys.
- Configure a shared replay store for multi-instance deployments; process-local replay protection is insufficient across replicas.
- Keep warrant lifetime short and constrain tenant, fields, operations and other dimensions as required by policy.
- Do not forward or persist raw access tokens in workflow payloads or event attributes.
- Fail closed when validation, tenant mapping, signing-key resolution or warrant validation fails.

**Source:** `src/csharp/Foundgine.Extensions/Keycloak` and `src/java/foundgine-extensions/src/main/java/com/foundgine/extensions/keycloak`.

## 4. Temporal durable-workflow integration

**Purpose.** Adds durable orchestration for long-running business processes. Temporal owns workflow state, timers, retries, signals and compensation. Foundgine owns authorization and execution of each semantic data operation.

**C# components**
- Process contracts and definitions.
- `FoundgineProcessWorkflow` and workflow starter/provider.
- Operation activities and activity policy.
- Outbox publisher bridge.
- Hosting/DI and configuration.
- HTTP status/start/decision/cancel endpoints.
- Diagnostics and security-context integration.

**Java components.** The Java module contains process contracts/state, workflow implementation/context, operation activities, workflow client/starter/provider, in-process workflow engine, outbox publisher, HTTP routes/options, diagnostics and Temporal security scope/context factory contracts.

**Division of responsibility**
- Event store (`Foundgine.Providers`): durable event history.
- Outbox (`Foundgine.Providers`): durable dispatch work, generally at-least-once.
- Temporal extension: orchestration, retries, timers, approvals and compensation.
- Foundgine runtime/gateway execution: authorization and execution of each data operation.

**Typical lifecycle**
1. A business transaction writes its outbox message.
2. Dispatcher publishes the message through `FoundgineTemporalOutboxPublisher`.
3. Temporal starts or signals a workflow using a stable business key.
4. Workflow schedules bounded activities.
5. Each activity obtains a trusted execution context and calls Foundgine for the operation.
6. Retry/compensation/approval behavior is governed by the workflow definition and activity policy.

**Security and reliability requirements**
- Do not put warrants or raw access tokens in workflow inputs/history.
- Re-establish or re-mint authority for each activity attempt and each Foundgine operation; activity retries are new execution attempts.
- Derive workflow tenant scope from trusted identity, not caller-supplied workflow IDs.
- Make activities idempotent or use stable operation/message identifiers because retries can repeat work.
- Configure payload encryption/codecs when workflow history contains sensitive business data.
- Protect approval/decision endpoints with a trusted role/policy and stamp the approver from the authenticated execution context.
- Run API and worker processes with appropriately separated credentials and signing keys.
- Treat outbox delivery as at-least-once, not exactly-once.

**Source:** `src/csharp/Foundgine.Extensions/Temporal` and `src/java/foundgine-extensions/src/main/java/com/foundgine/extensions/temporal`.

## 5. Extension registry and contracts

The Java extension module includes `FoundgineExtension`, `TransportExtension` and `FoundgineExtensionRegistry` as common extension descriptors/registry surfaces. These describe and organize installed integrations; they do not execute operations or bypass the runtime. The C# module's primary extension boundary is currently expressed through its package and framework-specific registration APIs.

## Package and dependency boundaries

- `Foundgine.Core` owns provider-independent semantic and security contracts.
- `Foundgine.Runtime` coordinates resolution, authorization, planning and execution.
- `Foundgine.Providers` supplies physical providers, gateway and MCP infrastructure.
- `Foundgine.Extensions` supplies optional framework/identity/workflow adapters.

C# package references should be selected according to the integration required; the main `Foundgine.Extensions` project references Core, Runtime and Providers. Java's `foundgine-extensions` module depends on `foundgine-core`, `foundgine-runtime` and `foundgine-providers`. These module-level dependencies do not mean every integration is mandatory at runtime; configure and use only the relevant extension.

## Hosting and dependency injection

Hosting extensions register core services, providers, capabilities, options and validation. Prefer explicit options and startup validation. Do not register multiple conflicting providers, and never let a missing security dependency silently fall back to permissive behaviour. Applications that never call `AddFoundgineTemporal*` never connect to Temporal.

## Insights and auditing for extensions

- Temporal emits metrics and traces under the source name `Foundgine.Temporal`. GraphQL and Keycloak rely on your host's normal logging plus Foundgine's own evidence and audit (see [Insights, evidence and auditing](insights-evidence-auditing)).
- Never put warrants or raw tokens into workflow history, events or logs.

## Integration author checklist

- Map external identity and context into trusted Foundgine contracts.
- Preserve tenant, subject, correlation and trace context.
- Delegate semantic resolution and authorization to Foundgine.
- Define retry, timeout, cancellation and idempotency behaviour.
- Add negative security tests and an end-to-end sample.
- Document provider and framework versions separately from Foundgine package versions.

## Adding an extension

1. Identify the external framework contract and the Foundgine contract it should adapt.
2. Keep framework parsing/serialization at the boundary.
3. Translate external input to provider-neutral semantic intent; never create a second semantic resolver.
4. Obtain trusted identity/scope from the host's validated security integration.
5. Delegate authorization, planning and execution to Foundgine.
6. Define cancellation, timeout, retry and idempotency semantics.
7. Add positive and negative security tests, including tenant isolation and missing/invalid identity.
8. Document dependencies, supported versions, configuration, hosting lifecycle and language-specific coverage.
9. Include a runnable sample and verify the published package/artifact, not only project-reference builds.

## Related pages

- [Provider catalogue](providers)
- [ORM providers (EF Core and JPA)](orm-providers)
- [Providers and storage](providers-storage)
- [Insights, evidence and auditing](insights-evidence-auditing)
- [Authorization and security](authorization-security)
- [Mutations](mutations)
- [Gateway, events and live updates](gateway-events)
- [Operations and testing](operations)
- [Cross-language implementation map](implementation-map)
