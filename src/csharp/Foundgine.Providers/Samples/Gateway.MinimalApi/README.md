# Foundgine Providers — ASP.NET Core Minimal API sample

This sample shows how one ASP.NET Core Minimal API host composes the provider interfaces shipped by `Foundgine.Providers`:

- InMemory execution and plan compilation
- SQL/PostgreSQL execution, plan compilation, mutation execution and batched mutation execution
- SQL provider cost estimation
- Elasticsearch lexical candidate retrieval and semantic lexicon indexing
- PostgreSQL/pgvector lexical candidate retrieval and semantic lexicon indexing
- PostgreSQL event store and Gateway event journal
- PostgreSQL transactional outbox
- MCP server-side tools and an outbound MCP agent client
- Microsoft.Extensions.AI toolset and agent facades
- Foundgine Gateway HTTP endpoints and provider capability discovery

The sample deliberately keeps the semantic model, generated `IMetadataProvider`, `IFoundgine` runtime facade, `IFoundgineMutations`, embedding generator and chat model application-owned. A provider package cannot invent the application's semantic model, authorization policy, model credentials or infrastructure.

## Configuration

Set the following values in user secrets, environment variables or deployment configuration. Do not commit production secrets.

| Setting | Purpose |
|---|---|
| `Authentication:Authority` | OIDC authority for JWT validation |
| `Authentication:Audience` | Expected API audience |
| `ConnectionStrings:Postgres` | Enables PostgreSQL SQL, event-store, gateway-journal and outbox integrations |
| `Elasticsearch:Uri` | Enables Elasticsearch lexical retrieval/indexing |
| `Providers:EnablePgVector` | Enables pgvector services when PostgreSQL is configured |
| `PgVector:Dimensions` | Embedding vector dimensions; must match the configured embedding model |
| `Mcp:BaseAddress` | Optional remote MCP host base address |

Example environment variables:

```bash
ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=foundgine;Username=foundgine;Password=local-dev"
Elasticsearch__Uri="http://localhost:9200"
Providers__EnablePgVector="true"
PgVector__Dimensions="1536"
```

The sample does not require PostgreSQL or Elasticsearch to start. The corresponding registrations are added only when their configuration is present. pgvector additionally requires the application to register `ISemanticEmbeddingGenerator` before resolving the pgvector services.

## Application-owned registrations

Before exercising semantic query endpoints, register the generated model metadata and Runtime facade. The exact generated metadata type is emitted by the AOT generator for the application's model, so it belongs in the consuming application:

```csharp
// Register the generated metadata type produced by Foundgine.Providers' AOT generator.
builder.Services.AddSingleton<IMetadataProvider, GeneratedMetadataProvider>();

// Register the configured Runtime facade and mutation facade from the application model.
builder.Services.AddSingleton<IFoundgine, FoundgineRuntime>();
builder.Services.AddSingleton<IFoundgineExecutor>(sp => sp.GetRequiredService<IFoundgine>());
builder.Services.AddSingleton<IFoundgineMutations, FoundgineMutationRuntime>();
```

`GeneratedMetadataProvider`, `FoundgineRuntime` and `FoundgineMutationRuntime` above are illustrative application types; use the concrete generated/runtime types from your application. The sample intentionally does not fabricate these types.

For pgvector, register an embedding implementation:

```csharp
builder.Services.AddSingleton<ISemanticEmbeddingGenerator, YourEmbeddingGenerator>();
```

For the AI agent, register a Microsoft.Extensions.AI chat client:

```csharp
builder.Services.AddSingleton<IChatClient>(yourConfiguredChatClient);
```

The MCP helpers register Foundgine tool handlers; the application still configures and maps its selected MCP transport. The outbound `FoundgineMcpAgentClient` is an HTTP client for a separate MCP server.

## Provider selection

There can be multiple `IExecutionProvider` and `IProviderPlanCompiler` implementations in the same process. The sample registers them as keyed services to avoid accidental last-registration-wins behavior:

```csharp
var compiler = app.Services.GetRequiredKeyedService<IProviderPlanCompiler>("postgres");
var executionProvider = app.Services.GetRequiredKeyedService<IExecutionProvider>("postgres");

var inMemoryCompiler = app.Services.GetRequiredKeyedService<IProviderPlanCompiler>("in-memory");
var inMemoryExecutionProvider = app.Services.GetRequiredKeyedService<IExecutionProvider>("in-memory");
```

Normal application requests should use `IFoundgine` from Runtime. Runtime owns semantic validation, authorization, planning and provider selection; endpoints should not accept provider names or raw SQL from callers.

## PostgreSQL schema setup

When PostgreSQL integrations are enabled, apply the provider's event-store schema before serving traffic:

```csharp
await using var scope = app.Services.CreateAsyncScope();
var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
await EventStoreMigrator.ApplyAsync(dataSource);
```

The sample runs `EventStoreMigrator.ApplyAsync` at startup when PostgreSQL is configured; it applies the bundled event-store, correlation and outbox migrations. In production, you may instead run the same migrator as a deployment step. Apply the application's own domain schema through your normal migration process. The gateway event journal is a system-level ordered stream, not tenant-partitioned domain data.

## Security notes

- The sample validates bearer tokens but does not implement warrant issuance/validation. Trusted middleware must set `HttpContext.Items[HttpContextSecurityExecutionContextProvider.TrustedContextItemKey]` only after validating the caller, tenant and Foundgine warrant.
- Never accept a `SecurityExecutionContext`, tenant identity, authorization predicate, SQL text, provider selection or warrant from request JSON.
- The in-memory Gateway approval store is development/single-instance only. Use a durable distributed `IFoundgineGatewayApprovalStore` in a multi-instance production deployment.
- PostgreSQL event journal and outbox are durable when configured. Run migrations before enabling traffic.
- Elasticsearch and pgvector provide lexical candidates only. The semantic contract and resolver remain authoritative.
- Do not expose `/providers` publicly without reviewing whether provider identity/capability disclosure is appropriate for your threat model.

## Run

```bash
dotnet run --project Gateway.MinimalApi.csproj
```

The project targets .NET 10 and references the local `Foundgine.Providers` project. It is a composition sample; the application-owned generated metadata/runtime registrations described above are required before resolving the semantic tool handlers or executing semantic requests.
