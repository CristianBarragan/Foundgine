# Foundgine Providers — Java Gateway sample (JDK `HttpServer`)

Java port of the C# `Gateway.MinimalApi` sample. One host composes the Gateway HTTP facade, an optional PostgreSQL
event journal and provider discovery, served by the JDK's built-in `com.sun.net.httpserver.HttpServer` through
`GatewayJdkHttpServer`. There is no web framework and no DI container; `GatewayMinimalApi.java` wires everything
explicitly.

## What maps to what

| C# `Program.cs` | Java |
|---|---|
| `AddFoundgineGateway` + `AddCapability` | `FoundgineGatewayOptions` + `FoundgineGateway` |
| `AddFoundgineGatewayInMemoryApprovalStore` | `InMemoryFoundgineGatewayApprovalStore` |
| `AddFoundgineGatewayPostgresEventStore` + `EventStoreMigrator.ApplyAsync` | `PostgresEventStore` + `PostgresGatewayEventStore` + `EventStoreMigrator.apply` (only when PostgreSQL is configured) |
| `MapFoundgineGateway()` | `FoundgineGatewayRoutes.map(services)` hosted by `GatewayJdkHttpServer` |
| `MapGet("/health")` | public `/health` route |
| `MapGet("/providers")` | `/providers`, anonymous like the C# route (see Security notes) |
| `HttpContextSecurityExecutionContextProvider` | a `ThreadLocal` set by the authorizer and cleared by `GatewayJdkHttpServer.Builder.afterRequest` |
| JWT bearer authentication | `DevTokenAuthorizer` (dev only). Replace it with real validation. |

The host now runs provider capability negotiation before opening its HTTP listener. The in-memory journal is registered
when PostgreSQL is not configured; when PostgreSQL is configured, startup additionally requires a durable event-store
provider. A failed requirement stops startup before any provider is published to the registry. The Postgres outbox,
Elasticsearch, pgvector, MCP and AI-agent registrations remain independent of the HTTP host and are composed through
their own Java types.

## Configuration

| Variable | C# equivalent | Purpose |
|---|---|---|
| `FOUNDGINE_PORT` | (Kestrel URLs) | Port, default `8080`; `0` picks a free port |
| `FOUNDGINE_HOST` | (Kestrel URLs) | Interface, default `127.0.0.1` |
| `FOUNDGINE_GATEWAY_NAME` | `Foundgine:GatewayName` | Gateway display name |
| `FOUNDGINE_POSTGRES_CONNECTION_STRING` | `ConnectionStrings:Postgres` | `jdbc:` URL or `Host=..;Database=..;Username=..;Password=..`; enables the durable journal |
| `FOUNDGINE_SAMPLE_DEV_TOKEN` | `Authentication:*` | Dev-only bearer token. Unset means every protected route answers 401. |
| `FOUNDGINE_SAMPLE_DEV_SUBJECT`, `FOUNDGINE_SAMPLE_DEV_TENANT` | | Identity the dev token maps to |

## Application-owned registrations

As in the C# sample, the semantic model, `IFoundgine`, `IFoundgineExecutor` and `IFoundgineMutations` belong to the
application. Set them in `GatewayMinimalApi.ApplicationRuntime` before `main` runs. Until then `/health`,
`/foundgine/capabilities`, `/foundgine/events` and `/providers` work, while `/foundgine/semantic` and the read/mutation
endpoints return 500 and log the reason on stderr.

## Run

Build the modules first, then:

```bash
FOUNDGINE_SAMPLE_DEV_TOKEN=change-me \
java -cp "<core-classes>:<runtime-classes>:<providers-classes>:jackson-annotations.jar:jackson-core.jar:jackson-databind.jar:postgresql.jar" \
     foundgine-providers/samples/gateway-minimal-api/GatewayMinimalApi.java
```

Then use `requests.http` (set `@token` to the same value).

## Security notes

- `HttpServer` speaks plain HTTP and binds to loopback by default. Terminate TLS in a reverse proxy before exposing it.
- `DevTokenAuthorizer` is a demo. A production host validates a real credential and the Foundgine warrant, and only then
  sets the `SecurityExecutionContext`. Never take a security context, tenant, authorization predicate, SQL text,
  provider selection or warrant from request JSON.
- The in-memory approval store is development/single-instance only; use a durable distributed
  `IFoundgineGatewayApprovalStore` for several instances.
- Each request, including each SSE stream, holds one worker thread (default pool: 64). Size it for the streams you
  allow, or pass a virtual-thread executor to the builder.
- `/providers` is anonymous, aligned with the C# sample: it discloses provider identity and capabilities to any caller. That is fine for a sample; put it behind authorization (set the route's `requireAuthorization` flag to `true`) or remove it before exposing a real host.
