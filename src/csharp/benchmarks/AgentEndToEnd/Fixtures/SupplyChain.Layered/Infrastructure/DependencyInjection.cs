using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.SupplyChain.Application;
using Foundgine.SupplyChain.Infrastructure.Mutations;
using Foundgine.SupplyChain.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Foundgine.SupplyChain.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSupplyChainInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));

        services.AddSingleton<IMetadataProvider>(_ =>
            SupplyChainSemanticConfiguration.Metadata);

        services.AddSingleton(
            SupplyChainSemanticConfiguration.Metadata);

        // SupplyChainSemanticConfiguration.Model is built but not frozen -
        // CreateSnapshot() intentionally never freezes implicitly (see
        // SemanticModel.CreateSnapshot()), so every other place in this
        // codebase that turns a model into a trusted contract snapshot calls
        // .Freeze() first. This call site was the one place that didn't,
        // which is why AddSupplyChainInfrastructure blew up with
        // "The semantic model must be frozen before it can be used as a
        // trusted semantic contract." as soon as anything tried to build a
        // WebApplicationFactory host for the PenTest suite.
        var frozenModel = SupplyChainSemanticConfiguration.Model.Freeze();

        services.AddSingleton(frozenModel);

        services.AddSingleton(
            frozenModel.CreateSnapshot());

        services.AddSingleton<Planner>();

        services.AddSingleton<SemanticSqlQueryExecutor>();

        services.AddScoped<ISupplyChainQueries, SupplyChainQueryRepository>();
        services.AddScoped<ISupplyChainMutations, SupplyChainMutationRepository>();

        return services;
    }
}