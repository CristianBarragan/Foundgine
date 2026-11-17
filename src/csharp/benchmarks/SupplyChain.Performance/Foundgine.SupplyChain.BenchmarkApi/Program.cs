using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Authorization;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Extensions.Keycloak.Hosting;
using Foundgine.Runtime;
using Foundgine.SupplyChain;
using Foundgine.SupplyChain.Domain;
using Foundgine.SupplyChain.Infrastructure;
using Foundgine.SupplyChain.BenchmarkApi;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Foundgine.Providers.Redis;
using Foundgine.Providers.Security.Warrants.Postgres;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("SupplyChain")
    ?? Environment.GetEnvironmentVariable(SupplyChainDomain.ConnectionStringName)
    ?? throw new InvalidOperationException(
        "ConnectionStrings:SupplyChain or SupplyChainConnectionString is required.");

var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
    ?? "redis:6379,abortConnect=false,syncTimeout=2000,connectTimeout=2000";
var redis = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
var dataSource = NpgsqlDataSource.Create(connection);
PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<RedisSecurityWarrantRevocationStore>(sp =>
    new RedisSecurityWarrantRevocationStore(new StackExchangeScriptBackend(sp.GetRequiredService<IConnectionMultiplexer>()), "benchmark:csharp:"));
builder.Services.AddSingleton<ISecurityWarrantRevocationEpochAnchor>(sp =>
    new PostgresSecurityWarrantRevocationEpochAnchor(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddSingleton<ISecurityWarrantRevocationStore>(sp =>
    new AnchoredSecurityWarrantRevocationStore(
        sp.GetRequiredService<RedisSecurityWarrantRevocationStore>(),
        sp.GetRequiredService<ISecurityWarrantRevocationEpochAnchor>()));

var metadata = SupplyChainDomain.Metadata;
var httpContextAccessor = new HttpContextAccessor();
builder.Services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
var semanticModel = metadata.Discover();

// Register the Foundgine EF provider against the canonical fluent SupplyChain metadata.
builder.Services.AddFoundgineEf(
    metadata,
    (sp, options) => options.UseNpgsql(
        connection,
        npgsql => npgsql.MigrationsAssembly("Foundgine.SupplyChain.BenchmarkApi")),
    options =>
    {
        SupplyChainEf.ConfigureModel(options);
        options.UseEngineProvider();
    });

// Keycloak JWT validation is the only HTTP identity source. The extension maps
// validated Keycloak roles into short-lived signed warrants and binds the tenant
// from the configured token claim. No GraphQL argument/header can override it.
var keycloak = builder.Services.AddFoundgineKeycloak(
    builder.Configuration.GetSection("Foundgine:Keycloak"));
if (builder.Configuration.GetValue<bool>("Foundgine:Keycloak:EphemeralWarrantKey"))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Ephemeral warrant signing keys are permitted only in Development.");
    keycloak.UseEphemeralWarrantKey();
}
builder.Services.AddSingleton<ISecurityWarrantReplayStore, MemorySecurityWarrantReplayStore>();
builder.Services.AddSingleton<IMutationSchema>(metadata);

// AddFoundgineEf registers FoundgineEfReader, which needs the planner and the semantic authorizer. This host composes its
// own services, so it must register them. The authorizer uses this host's warrant policy (never allow-all).
builder.Services.AddSingleton<Foundgine.Core.Semantic.Planning.Planner>();
builder.Services.AddSingleton(new SemanticAuthorizer(new WarrantSemanticAuthorizationPolicy(httpContextAccessor)));

builder.Services.AddSingleton<FactoryEfCoreMutationExecutionProvider>();
builder.Services.AddFoundgine(options =>
{
    options.Model = semanticModel;
    options.AuthorizationPolicy = new WarrantSemanticAuthorizationPolicy(httpContextAccessor);
    options.ExpectedWarrantIssuer = builder.Configuration["Foundgine:Keycloak:WarrantIssuer"]
        ?? "foundgine-keycloak-bridge";
    options.MutationSchema = metadata; // MetadataRegistry implements IMutationSchema.
    options.MutationProviderFactory = sp =>
        sp.GetRequiredService<FactoryEfCoreMutationExecutionProvider>();
});

builder.Services.AddSingleton(semanticModel);
builder.Services.AddScoped(_ => new HotChocolateSemanticAdapter(semanticModel));
builder.Services.AddScoped(_ => new HotChocolateMutationAdapter(semanticModel, metadata));
builder.Services.AddScoped(sp => new FoundgineGraphQLQueryExecutor(
    sp.GetRequiredService<IFoundgine>(),
    sp.GetRequiredService<HotChocolateSemanticAdapter>(),
    sp.GetRequiredService<ISecurityExecutionContextProvider>(),
    () =>
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.IsInRole("sc-customer") == true &&
            int.TryParse(principal.FindFirst("customer_id")?.Value, out var customerId))
            values["user.CustomerId"] = customerId;
        return new ExecutionContext(values);
    }));
builder.Services.AddScoped<FoundgineGraphQLMutationExecutor>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStart"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<FoundgineDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", arm = "foundgine-efcore-hotchocolate" }));
app.MapGet("/health/ready", async (FoundgineDbContext db, ISecurityWarrantRevocationStore revocations,
    RedisSecurityWarrantRevocationStore redisStore, CancellationToken ct) =>
{
    if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    _ = redisStore.Probe();
    _ = revocations.CurrentSequence;
    return Results.Ok(new { status = "ready", securityState = "anchored" });
});

app.MapFoundgineGraphQL();
app.Run();

public partial class Program;
