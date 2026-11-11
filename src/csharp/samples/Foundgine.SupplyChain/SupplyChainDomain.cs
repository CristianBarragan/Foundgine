using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.Runtime;
using Foundgine.SupplyChain.Application;
using Foundgine.SupplyChain.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundgine.SupplyChain;

/// <summary>
/// Everything the old <c>Application/DependencyInjection.cs</c> and
/// <c>Infrastructure/DependencyInjection.cs</c> used to register (see
/// <c>src/csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered</c> for the layered version this sample
/// grew out of), folded into one capability now that it's a single project: the per-actor
/// <see cref="ICapabilityAuthorizer"/>, the <see cref="SupplyChainApplication"/> facade, the EF Core
/// provider registration and the metadata registry compiled from the fluent model.
///
/// Also supplies the <see cref="Foundgine.Core.Semantic.SemanticModel"/> Foundgine itself needs (via
/// <see cref="FoundgineOptions.UseMetadata"/>) and a permissive Foundgine authorization policy: this
/// sample authorizes at the application layer (actor + token + capability, see
/// <see cref="SupplyChainAuthorizer"/>) rather than through Foundgine's own semantic authorization
/// pipeline, which nothing in this sample's MCP tools currently routes through.
/// </summary>
public sealed class SupplyChainDomain : IFoundgineCapability
{
    /// <summary>Configuration key / environment variable name for the Postgres connection string.</summary>
    public const string ConnectionStringName = "SupplyChainConnectionString";

    // Compiled once from the fluent configuration in Domain/ (SupplyChainModel).
    private static readonly MetadataRegistry Metadata = new SupplyChainModel().Build();

    public static void Configure(FoundgineCapabilityContext context)
    {
        var services = context.Services;

        context.Options.UseMetadata(Metadata);
        context.Options.AuthorizationPolicy ??= new AllowAllSemanticAuthorizationPolicy();

        // Registration: the EF provider turns the fluent model into the EF model, the ORM maps, the DbContext,
        // IFoundgineRepository<T> for every entity and the semantic reader. One call.
        services.AddFoundgineEf(Metadata,
            (sp, o) => o.UseNpgsql(ResolveConnectionString(sp.GetRequiredService<IConfiguration>())));

        services.AddSingleton(new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()));
        services.AddSingleton<Planner>();
        services.AddSingleton<ICapabilityAuthorizer, SupplyChainAuthorizer>();
        services.AddScoped<OrderWorkflows>();
        services.AddScoped<SupplyChainApplication>();
    }

    private static string ResolveConnectionString(IConfiguration configuration) =>
        configuration[ConnectionStringName]
        ?? Environment.GetEnvironmentVariable(ConnectionStringName)
        ?? throw new InvalidOperationException($"{ConnectionStringName} is required.");
}

/// <summary>Fluent <c>Use</c> surface for <see cref="SupplyChainDomain"/>.</summary>
public static class SupplyChainDomainFoundgineOptionsExtensions
{
    /// <summary>Enables <see cref="SupplyChainDomain"/>. Equivalent to <c>options.Enable&lt;SupplyChainDomain&gt;()</c>.</summary>
    public static FoundgineOptions UseSupplyChainDomain(this FoundgineOptions options) =>
        options.Enable<SupplyChainDomain>();
}
