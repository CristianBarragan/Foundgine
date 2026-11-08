using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Execution.Security;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Providers.EventStore;
using Foundgine.Providers.EventStore.Schema;
using Foundgine.Providers.Gateway;
using Foundgine.Providers.Gateway.Providers;
using Foundgine.Providers.Models;
using Foundgine.Providers.Outbox;
using Foundgine.Providers.Storage.Elasticsearch;
using Foundgine.Providers.Storage.InMemory;
using Foundgine.Providers.Storage.PostgresVector;
using Foundgine.Providers.Storage.Sql;
using Foundgine.Providers.Storage.Sql.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation.Postgres;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Providers.Tools.MCP.Client;
using Foundgine.Runtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Authentication and trusted execution context. A production host must populate
// HttpContext.Items only after validating the identity, tenant and Foundgine warrant.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISecurityExecutionContextProvider, HttpContextSecurityExecutionContextProvider>();

// The application registers its generated metadata/model and Runtime facade here.
// For example: IMetadataProvider -> generated metadata; IFoundgine -> configured runtime.
// These are application-owned contracts, not provider-specific defaults.

// In-memory execution provider (useful for local development, tests and deterministic demos).
builder.Services.AddSingleton<InMemoryDataSet>();
builder.Services.AddSingleton<InMemoryCompiler>(sp => new InMemoryCompiler(
    sp.GetRequiredService<IMetadataProvider>(), sp.GetRequiredService<InMemoryDataSet>()));
builder.Services.AddKeyedSingleton<IProviderPlanCompiler>("in-memory",
    (sp, _) => sp.GetRequiredService<InMemoryCompiler>());
builder.Services.AddKeyedSingleton<ISecurityInvariantProviderCompiler>("in-memory",
    (sp, _) => sp.GetRequiredService<InMemoryCompiler>());
builder.Services.AddKeyedSingleton<IProviderSecurityConformanceEvaluator>("in-memory",
    (sp, _) => sp.GetRequiredService<InMemoryCompiler>());
builder.Services.AddKeyedSingleton<IExecutionProvider>("in-memory",
    (sp, _) => new InMemoryExecutionProvider(
        sp.GetRequiredService<IMetadataProvider>(), sp.GetRequiredService<InMemoryDataSet>()));

// PostgreSQL/SQL provider interfaces. The data source is optional so the sample can
// start without infrastructure; configure ConnectionStrings:Postgres to enable it.
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(postgresConnectionString));
    builder.Services.AddScoped(sp => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection());

    builder.Services.AddKeyedSingleton<SqlCompiler>("postgres", (sp, _) =>
        new SqlCompiler(sp.GetRequiredService<IMetadataProvider>()));
    builder.Services.AddKeyedSingleton<IProviderPlanCompiler>("postgres",
        (sp, _) => sp.GetRequiredKeyedService<SqlCompiler>("postgres"));
    builder.Services.AddKeyedSingleton<ISecurityInvariantProviderCompiler>("postgres",
        (sp, _) => sp.GetRequiredKeyedService<SqlCompiler>("postgres"));
    builder.Services.AddKeyedSingleton<IProviderSecurityConformanceEvaluator>("postgres",
        (sp, _) => sp.GetRequiredKeyedService<SqlCompiler>("postgres"));
    builder.Services.AddKeyedScoped<IExecutionProvider>("postgres", (sp, _) =>
        new SqlExecutionProvider(sp.GetRequiredService<NpgsqlConnection>()));

    builder.Services.AddKeyedScoped<IMutationExecutionProvider>("postgres", (sp, _) =>
        new SqlMutationExecutionProvider(
            sp.GetRequiredService<NpgsqlConnection>(),
            metadata: sp.GetRequiredService<IMetadataProvider>()));
    builder.Services.AddKeyedScoped<IMutationBatchExecutionProvider>("postgres", (sp, _) =>
        new PostgresBatchedMutationExecutionProvider(
            sp.GetRequiredService<NpgsqlConnection>(),
            sp.GetRequiredService<IMetadataProvider>()));
    builder.Services.AddKeyedScoped<IMutationSecurityConformanceEvaluator>("postgres", (sp, _) =>
        (IMutationSecurityConformanceEvaluator)sp.GetRequiredKeyedService<IMutationBatchExecutionProvider>("postgres"));

    // Durable event journal and transactional outbox.
    builder.Services.AddFoundgineGatewayPostgresEventStore();
    builder.Services.AddSingleton<IFoundgineProvider>(sp =>
        (IFoundgineProvider)sp.GetRequiredService<IEventStore>());
    builder.Services.AddSingleton<IFoundgineProvider>(sp =>
        sp.GetRequiredService<PostgresGatewayEventStore>());
    builder.Services.AddFoundgineGatewayPostgresOutbox();

    builder.Services.AddSingleton<IProviderCostEstimator>(sp =>
        new SqlCostEstimator(sp.GetRequiredService<IMetadataProvider>()));
}

// Elasticsearch lexical candidate retrieval. This is a candidate source only;
// Foundgine's semantic resolver remains authoritative for topology and authorization.
var elasticsearchUri = builder.Configuration["Elasticsearch:Uri"];
if (Uri.TryCreate(elasticsearchUri, UriKind.Absolute, out var elasticsearchBaseUri))
{
    builder.Services.AddHttpClient<ElasticsearchSemanticLexicalCandidateSource>(client =>
        client.BaseAddress = elasticsearchBaseUri);
    builder.Services.AddTransient<ISemanticLexicalCandidateSource>(sp =>
        sp.GetRequiredService<ElasticsearchSemanticLexicalCandidateSource>());
    builder.Services.AddHttpClient<SemanticLexiconIndexClient>(client =>
        client.BaseAddress = elasticsearchBaseUri);
}

// pgvector candidate retrieval is enabled when PostgreSQL is configured AND the
// application supplies an ISemanticEmbeddingGenerator (for example an AI embedding model).
if (!string.IsNullOrWhiteSpace(postgresConnectionString) &&
    builder.Configuration.GetValue<bool>("Providers:EnablePgVector"))
{
    builder.Services.AddSingleton(_ => new PgVectorOptions(
        Dimensions: builder.Configuration.GetValue<int?>("PgVector:Dimensions") ?? 1536));
    builder.Services.AddTransient<PgVectorSemanticLexicalCandidateSource>();
    builder.Services.AddTransient<ISemanticLexicalCandidateSource>(sp =>
        sp.GetRequiredService<PgVectorSemanticLexicalCandidateSource>());
    builder.Services.AddSingleton<PgVectorSemanticLexiconIndexClient>();
}

// MCP server-side tool handlers. The hosting application still owns the MCP transport.
builder.Services.AddSingleton<Func<Foundgine.Core.Execution.ExecutionContext>>(
    () => new Foundgine.Core.Execution.ExecutionContext());
builder.Services.AddFoundgineMcp();

// MCP client for calling another Foundgine MCP host (optional).
builder.Services.AddHttpClient<FoundgineMcpAgentClient>(client =>
{
    var endpoint = builder.Configuration["Mcp:BaseAddress"];
    if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) client.BaseAddress = uri;
});

// AI toolset and agent: the application must register IFoundgine, IChatClient and
// a trusted security-context source before resolving these services.
builder.Services.AddTransient<FoundgineAiToolset>(sp => new FoundgineAiToolset(
    sp.GetRequiredService<IFoundgine>(),
    securityContextFactory: () => sp.GetRequiredService<ISecurityExecutionContextProvider>()
        .GetSecurityExecutionContext()));
builder.Services.AddTransient<FoundgineAiAgent>(sp => new FoundgineAiAgent(
    sp.GetRequiredService<IChatClient>(), sp.GetRequiredService<FoundgineAiToolset>()));

// Gateway HTTP facade. In-memory approval/event stores are for development and
// single-instance deployments only. PostgreSQL replaces the event journal above.
builder.Services.AddFoundgineGatewayInMemoryApprovalStore();
builder.Services.AddFoundgineGateway(options =>
{
    options.Name = builder.Configuration["Foundgine:GatewayName"] ?? "Foundgine Minimal API";
    options.ContractVersion = "1.1";
    options.AddCapability(new FoundgineGatewayCapability(
        "semantic.read", "Semantic read", "1.0", "query",
        "Execute authorized provider-neutral read intents.", ["POST /foundgine/read"]));
});

var app = builder.Build();
if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    await EventStoreMigrator.ApplyAsync(
        migrationScope.ServiceProvider.GetRequiredService<NpgsqlDataSource>());
}
app.UseAuthentication();
app.UseAuthorization();

app.MapFoundgineGateway();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Reports which optional provider integrations are configured without exposing secrets.
app.MapGet("/providers", (IEnumerable<IFoundgineProvider> providers) => Results.Ok(
    providers.Select(provider => new
    {
        provider.Id,
        provider.Name,
        provider.Version,
        Kind = provider.Kind.ToString(),
        Capabilities = provider.Capabilities.ToString()
    })));

// Provider-neutral application endpoints should call IFoundgine (Runtime), not
// execute provider plans directly. The keyed low-level registrations above are
// intended for explicit provider selection, diagnostics and advanced scenarios.
app.Run();

/// <summary>Reads a context established by trusted authentication/warrant middleware.</summary>
public sealed class HttpContextSecurityExecutionContextProvider(IHttpContextAccessor accessor)
    : ISecurityExecutionContextProvider
{
    public static readonly object TrustedContextItemKey = new();

    public SecurityExecutionContext? GetSecurityExecutionContext()
    {
        var items = accessor.HttpContext?.Items;
        return items is not null && items.TryGetValue(TrustedContextItemKey, out var value)
            ? value as SecurityExecutionContext
            : null;
    }
}
