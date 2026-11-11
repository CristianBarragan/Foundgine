using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Runtime;
using Foundgine.Runtime.Capabilities;
using Foundgine.SupplyChain.Advanced.OpenIntent;
using Foundgine.SupplyChain.Advanced.OpenIntent.Api;
using Foundgine.SupplyChain.Advanced.OpenIntent.Repositories;
using Foundgine.SupplyChain.Advanced.OpenIntent.Services;
using Npgsql;

// Composition root only. Layers: Api (MCP transport) -> Services (authorization, rules, error shaping, generic
// entities) -> Repositories (the preserved complex queries/mutations) -> EF Core / Npgsql.
var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration["SupplyChainConnectionString"]
         ?? Environment.GetEnvironmentVariable("SupplyChainConnectionString")
         ?? throw new InvalidOperationException("SupplyChainConnectionString is required.");

var semanticModel = OpenIntentSemanticModel.Model;
var dataSource = NpgsqlDataSource.Create(cs);

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(semanticModel);
builder.Services.AddSingleton<ISemanticLexicalCandidateSource, SemanticContractLexicalCandidateSource>();

// Semantic reads and the generic entity CRUD run through the EF Core provider. Hand-written Npgsql in the
// repository (advisory locks, FOR UPDATE SKIP LOCKED, idempotency tables, pg_trgm/full-text) stays native:
// EF Core does not express it.
builder.Services.AddOpenIntentPersistence(dataSource);

// The closed capability surface uses its application-level authorizer. The open-intent engine has its own
// semantic policy below; keeping the two explicit prevents a transport adapter from silently becoming the
// application's authorization authority.
builder.Services.AddSingleton(new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()));
builder.Services.AddSingleton<SupplyChainAuthorizer>();
builder.Services.AddSingleton<Planner>();
builder.Services.AddScoped<ISupplyChainRepository, SupplyChainExecutionService>();
builder.Services.AddScoped<SupplyChainService>();

// Foundgine's provider-neutral read pipeline: intent -> semantic graph -> authorization -> plan -> Execution IR
// -> provider (EF Core, registered above).
builder.Services.AddFoundgine(options =>
{
    options.Model = semanticModel;
    options.UseGrounding();
    options.AuthorizationPolicy = new AllowAllSemanticAuthorizationPolicy();
    options.WarrantKeyResolver = new OpenIntentDemoSecurity.KeyResolver();
    options.ExpectedWarrantIssuer = OpenIntentDemoSecurity.ExpectedIssuer;
    options.WarrantReplayStore = new MemorySecurityWarrantReplayStore();
});

// The MCP adapter itself is deliberately unaware of actor/token authentication. The host establishes
// SecurityExecutionContext before the adapter can execute.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISecurityExecutionContextProvider, HttpContextSecurityExecutionContextProvider>();
builder.Services.AddFoundgineMcp(() => new Foundgine.Core.Execution.ExecutionContext());

builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<SupplyChainMcpTools>()
    .WithTools<FoundgineMcpTools>();

var app = builder.Build();
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.Use(async (ctx, next) =>
    {
        await OpenIntentDemoSecurity.PopulateSecurityContextAsync(ctx);
        await next();
    }));
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (NpgsqlDataSource ds, CancellationToken ct) =>
{
    await using var c = await ds.OpenConnectionAsync(ct);
    await using var cmd = new NpgsqlCommand("SELECT 1", c);
    await cmd.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ready" });
});
app.Run();
