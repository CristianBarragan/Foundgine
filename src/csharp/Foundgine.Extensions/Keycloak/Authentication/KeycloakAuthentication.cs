using System.Security.Claims;
using Foundgine.Extensions.Keycloak.Claims;
using Foundgine.Extensions.Keycloak.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Foundgine.Extensions.Keycloak.Authentication;

internal static class KeycloakAuthentication
{
    public static void Add(IServiceCollection services, FoundgineKeycloakOptions options)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                if (!string.IsNullOrWhiteSpace(options.MetadataAddress)) jwt.MetadataAddress = options.MetadataAddress;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.MapInboundClaims = false; // keep "sub", "realm_access" etc. exactly as Keycloak issues them
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,          // issuer comes from the realm's discovery document
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = options.ClockSkew,
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role
                };
                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal is not null) KeycloakClaims.AddRoleClaims(context.Principal, options);
                        return Task.CompletedTask;
                    }
                };
            });
    }
}
