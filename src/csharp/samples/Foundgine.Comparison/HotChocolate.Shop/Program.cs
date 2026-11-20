using System.Text;
using HotChocolate.Shop;
using HotChocolate.Shop.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("Shop") ?? "Host=localhost;Port=5433;Database=shop;Username=shop;Password=shop";
var key = builder.Configuration["Jwt:SigningKey"] ?? "dev-only-signing-key-change-me-32bytes!!";

builder.Services.AddDbContext<ShopDbContext>(o => o.UseNpgsql(cs));

// ---- Authentication: plain JWT bearer (identical block in Foundgine.Shop) ----
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
builder.Services.AddAuthorization(o => o.FallbackPolicy =
    new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim("tenant_id").Build());

builder.Services.AddGraphQLServer().AddAuthorization()
    .AddQueryType<Query>().AddFiltering().AddSorting();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapGraphQL("/graphql");
app.Run();
