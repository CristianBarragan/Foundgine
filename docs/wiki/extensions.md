# Framework extensions and integrations

Extensions adapt a framework, identity system or workflow engine to Foundgine's contracts. They are optional: applications should install only the integrations they use. An extension is an adapter, not a second semantic engine. It must translate external requests or context into Foundgine contracts and delegate semantic resolution, authorization and execution to the core/runtime boundary.

## Extension selection at a glance

| Extension | Purpose | C# source | Java source |
|---|---|---|---|
| GraphQL | Translate GraphQL schema operations, selections, variables and mutations into Foundgine semantic intent and results | `src/csharp/Foundgine.Extensions/GraphQL/HotChocolate` | `src/java/foundgine-extensions/.../graphql` |
| Keycloak | Validate Keycloak identity tokens, map claims, establish trusted execution context and issue/resolve Foundgine warrants | `src/csharp/Foundgine.Extensions/Keycloak` | `src/java/foundgine-extensions/.../keycloak` |
| Temporal | Durable workflow orchestration, activities, process lifecycle, outbox bridge, security propagation and HTTP routes | `src/csharp/Foundgine.Extensions/Temporal` | `src/java/foundgine-extensions/.../temporal` |
| Extension registry | Common extension identity/registration contracts | `src/csharp/Foundgine.Extensions` | `com.foundgine.extensions` |

> Both language modules contain GraphQL, Keycloak and Temporal source trees in this repository snapshot. That does not establish exact API, feature or maturity parity. Check each language's implementation and tests before assuming equivalent hosting, provider or operational behavior.

## GraphQL integration

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

## Keycloak identity integration

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

## Temporal durable-workflow integration

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

## Extension registry and contracts

The Java extension module includes `FoundgineExtension`, `TransportExtension` and `FoundgineExtensionRegistry` as common extension descriptors/registry surfaces. These describe and organize installed integrations; they do not execute operations or bypass the runtime. The C# module's primary extension boundary is currently expressed through its package and framework-specific registration APIs.

## Package and dependency boundaries

- `Foundgine.Core` owns provider-independent semantic and security contracts.
- `Foundgine.Runtime` coordinates resolution, authorization, planning and execution.
- `Foundgine.Providers` supplies physical providers, gateway and MCP infrastructure.
- `Foundgine.Extensions` supplies optional framework/identity/workflow adapters.

C# package references should be selected according to the integration required; the main `Foundgine.Extensions` project references Core, Runtime and Providers. Java's `foundgine-extensions` module depends on `foundgine-core`, `foundgine-runtime` and `foundgine-providers`. These module-level dependencies do not mean every integration is mandatory at runtime; configure and use only the relevant extension.

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

- [Providers and storage](providers-storage.md)
- [Authorization and security](authorization-security.md)
- [Mutations](mutations.md)
- [Gateway, events and live updates](gateway-events.md)
- [Operations and testing](operations.md)
- [Cross-language implementation map](implementation-map.md)
