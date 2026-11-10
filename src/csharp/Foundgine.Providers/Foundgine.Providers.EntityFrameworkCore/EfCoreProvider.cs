using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Providers.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.EntityFrameworkCore;

public static class FoundgineEfCore
{
    /// <summary>Provider name stamped on plans and execution evidence.</summary>
    public const string ProviderName = "efcore";
}

/// <summary>Compiles ExecutionIR into an EF Core plan (validation + authorization provenance binding).</summary>
public sealed class EfCorePlanCompiler : OrmPlanCompiler
{
    public EfCorePlanCompiler(OrmRegistry registry, OrmProviderCapabilities? capabilities = null) : base(FoundgineEfCore.ProviderName, registry, capabilities ?? OrmProviderCapabilities.SharedLinq(FoundgineEfCore.ProviderName)) { }
}

/// <summary>Executes EF Core plans against a <see cref="DbContext"/>. The context is borrowed, never disposed.</summary>
public sealed class EfCoreExecutionProvider : IExecutionProvider
{
    private readonly OrmExecutionProvider _inner;

    public EfCoreExecutionProvider(DbContext context, OrmRegistry registry) =>
        _inner = new OrmExecutionProvider(FoundgineEfCore.ProviderName, new EfCoreQuerySource(context), registry);

    public Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan, ExecutionContext context, CancellationToken cancellationToken = default) =>
        _inner.ExecuteAsync(plan, context, cancellationToken);
}

public static class FoundgineEfCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core compiler (singleton) and execution provider (scoped, bound to the scoped
    /// <typeparamref name="TContext"/>). Build the registry once with
    /// <c>OrmRegistry.Create(metadata.Entities, GeneratedOrmMaps.CreateAll())</c>.
    /// </summary>
    public static IServiceCollection AddFoundgineEntityFrameworkCore<TContext>(
        this IServiceCollection services, OrmRegistry registry) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);

        services.AddSingleton(registry);
        services.AddSingleton<IProviderPlanCompiler>(new EfCorePlanCompiler(registry));
        services.AddScoped<IExecutionProvider>(sp =>
            new EfCoreExecutionProvider(sp.GetRequiredService<TContext>(), registry));
        services.AddScoped<IMutationBatchExecutionProvider>(sp =>
            new EfCoreMutationExecutionProvider(sp.GetRequiredService<TContext>(), registry));
        return services;
    }
}
