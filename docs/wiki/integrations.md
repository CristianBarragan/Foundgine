# Framework and infrastructure integrations

Foundgine integrations adapt external infrastructure to Foundgine contracts. They should not create a parallel semantic engine or move authorization decisions into transport-specific code.

## Keycloak

The Keycloak integration connects authenticated identity and token claims to a trusted Foundgine security execution context. It includes token validation/key resolution, claim mapping, service-token support, warrant issuance/key resolution, and administrative operations where included by the implementation.

Use Keycloak for identity lifecycle and authentication. Use Foundgine authorization/warrant contracts to express what the authenticated principal may exercise. Never accept tenant or role claims from unverified request payloads; validate tokens, issuer, audience, signature, expiry, and key rotation behavior.

Source: `src/csharp/Foundgine.Extensions/Keycloak` and `src/java/foundgine-extensions/.../keycloak`.

## Temporal

The Temporal integration maps Foundgine operations into durable workflow/activity execution. Workflow code should orchestrate; activities should perform bounded calls to Foundgine or external services. Security context propagation must be explicit and validated when work crosses process or time boundaries. A workflow history is not an authorization grant; revalidate authority at activity execution where required.

Outbox publication can connect committed business operations to workflow/event processing. Idempotency and retry semantics are essential because workflow activities may be retried.

Source: `src/csharp/Foundgine.Extensions/Temporal` and `src/java/foundgine-extensions/.../temporal`.

## GraphQL / Hot Chocolate

The GraphQL extension adapts schema fields, directives, variables, query/mutation execution, and result shaping to Foundgine semantic contracts. It should translate GraphQL input into semantic intent and delegate authorization and execution to Foundgine. GraphQL schema visibility is not a substitute for runtime authorization.

The C# Hot Chocolate adapter lives under `src/csharp/Foundgine.Extensions/GraphQL/HotChocolate`. Check the Java extension module for the corresponding adapter surface before assuming equivalent framework coverage.

## Hosting and dependency injection

Hosting extensions register core services, providers, capabilities, options, and validation. Prefer explicit options and startup validation. Do not register multiple conflicting providers or allow a missing security dependency to silently fall back to permissive behavior.

## Integration author checklist

- Map external identity/context into trusted Foundgine contracts.
- Preserve tenant, subject, correlation, and trace context.
- Delegate semantic resolution and authorization to Foundgine.
- Define retry, timeout, cancellation, and idempotency behavior.
- Add negative security tests and end-to-end samples.
- Document provider/framework versions separately from Foundgine package versions.


## Full extension catalogue

For the complete GraphQL, Keycloak and Temporal extension inventory, source paths, trust boundaries and production guidance, see the [Extension catalogue](extensions.md).
