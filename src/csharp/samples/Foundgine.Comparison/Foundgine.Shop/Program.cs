using System.Text;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Foundgine.Extensions.GraphQL.Http;
using Foundgine.Extensions.Keycloak.Hosting;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.Runtime;
using Foundgine.Shop;
using Foundgine.Shop.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("Shop") ?? "Host=localhost;Port=5433;Database=shop;Username=shop;Password=shop";
var key = builder.Configuration["Jwt:SigningKey"] ?? "dev-only-signing-key-change-me-32bytes!!";

// ---- Authentication: plain JWT bearer (identical block in HotChocolate.Shop) ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = "https://auth.local", ValidAudience = "shop-api",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        NameClaimType = "sub", RoleClaimType = "role", ClockSkew = TimeSpan.FromSeconds(5)
    };
});
builder.Services.AddAuthorization();
var accessor = new HttpContextAccessor();
builder.Services.AddSingleton<IHttpContextAccessor>(accessor);

// ---- Foundgine: model, EF provider, warrant bridge, engine ----
var metadata = new ShopModel().Build();
var semanticModel = metadata.Discover();
builder.Services.AddFoundgineEf(metadata, (_, o) => o.UseNpgsql(cs), o => o.UseEngineProvider());

builder.Services.AddFoundgineKeycloakWarrants(o =>
{
    o.WarrantIssuer = "shop-jwt-bridge"; o.WarrantAudience = "foundgine"; ShopAuth.MapGrants(o);
}).UseEphemeralWarrantKey(); // single instance only; use a mounted key / KMS provider when scaling out
builder.Services.AddSingleton<ISecurityExecutionContextProvider, JwtSecurityContextProvider>();
builder.Services.AddSingleton<ISecurityWarrantReplayStore, MemorySecurityWarrantReplayStore>();
builder.Services.AddSingleton<ISecurityWarrantRevocationStore, MemorySecurityWarrantRevocationStore>();

builder.Services.AddFoundgine(o =>
{
    o.Model = semanticModel;
    o.AuthorizationPolicy = new ShopAuthorizationPolicy(accessor); // Planner + SemanticAuthorizer are registered by AddFoundgine
    o.ExpectedWarrantIssuer = "shop-jwt-bridge";
});

// What a customer's row-level rule may reference (validated claim, never GraphQL input). Shared by every protocol below.
ExecutionContext ShopContext()
{
    var values = new Dictionary<string, object?>(StringComparer.Ordinal);
    var user = accessor.HttpContext?.User;
    if (user?.IsInRole("customer") == true && int.TryParse(user.FindFirst("customer_id")?.Value, out var id))
        values["user.CustomerId"] = id;
    return new ExecutionContext(values);
}

// ---- Protocol 1: GraphQL ----
builder.Services.AddSingleton(semanticModel);
builder.Services.AddScoped(_ => new HotChocolateSemanticAdapter(semanticModel));
builder.Services.AddScoped(sp => new FoundgineGraphQLQueryExecutor(
    sp.GetRequiredService<IFoundgine>(),
    sp.GetRequiredService<HotChocolateSemanticAdapter>(),
    sp.GetRequiredService<ISecurityExecutionContextProvider>(),
    ShopContext));

// ---- Protocol 2 (opt-in, Mcp:Enabled=true): MCP. Same model, same row-level policy, same JWT. No new mapping code. ----
var mcp = builder.Configuration.GetValue<bool>("Mcp:Enabled");
if (mcp) builder.Services.AddShopMcp(ShopContext);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapFoundgineGraphQL(ShopGraphQL.Configure);
if (mcp) app.MapShopMcp();
app.Run();
