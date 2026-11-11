using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.EntityFrameworkCore;

public static class FoundgineEfServiceCollectionExtensions
{
    /// <summary>
    /// One-line EF Core registration for a Foundgine model:
    /// <code>services.AddFoundgineEf(new SupplyChainModel().Build(), (sp, o) =&gt; o.UseNpgsql(cs));</code>
    /// Registers the compiled <see cref="FoundgineEfModel"/> and <see cref="OrmRegistry"/>, a
    /// <see cref="FoundgineDbContext"/> (scoped; created from a factory so singletons can create their own),
    /// <see cref="IFoundgineRepository{T}"/> for every entity, <see cref="FoundgineEfReader"/> and the EF plan compiler.
    /// Every registration uses <c>TryAdd</c>, so any of them can be replaced before or after this call.
    /// </summary>
    public static IServiceCollection AddFoundgineEf(
        this IServiceCollection services,
        MetadataRegistry metadata,
        Action<IServiceProvider, DbContextOptionsBuilder> configureDb,
        Action<FoundgineEfOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(configureDb);

        var options = new FoundgineEfOptions();
        configure?.Invoke(options);
        var model = new FoundgineEfModel(metadata, options); // fails here, at startup, on any model/ORM mismatch

        services.TryAddSingleton(metadata);
        services.TryAddSingleton<IMetadataProvider>(metadata);
        services.TryAddSingleton<IMetadataCatalog>(metadata);
        services.TryAddSingleton(model);
        services.TryAddSingleton(model.Registry);
        services.TryAddSingleton(new EfCorePlanCompiler(model.Registry, model.Capabilities));

        services.AddDbContextFactory<FoundgineDbContext>(configureDb);
        services.TryAddScoped(sp => sp.GetRequiredService<IDbContextFactory<FoundgineDbContext>>().CreateDbContext());
        services.TryAddScoped(typeof(IFoundgineRepository<>), typeof(FoundgineEfRepository<>));
        services.TryAddScoped<FoundgineEfReader>();
        services.TryAddScoped<FoundgineEntities>();

        if (options.RegisterEngineProvider)
        {
            services.TryAddSingleton<IProviderPlanCompiler>(sp => sp.GetRequiredService<EfCorePlanCompiler>());
            services.TryAddSingleton<IExecutionProvider, FactoryEfCoreExecutionProvider>();
        }

        return services;
    }

    /// <summary>Overload for options that do not need the service provider.</summary>
    public static IServiceCollection AddFoundgineEf(
        this IServiceCollection services,
        MetadataRegistry metadata,
        Action<DbContextOptionsBuilder> configureDb,
        Action<FoundgineEfOptions>? configure = null) =>
        services.AddFoundgineEf(metadata, (_, o) => configureDb(o), configure);
}

/// <summary>
/// Execution provider for singleton engines: a fresh <see cref="FoundgineDbContext"/> per execution, from the
/// factory, so the engine never captures a scoped context.
/// </summary>
public sealed class FactoryEfCoreExecutionProvider(
    IDbContextFactory<FoundgineDbContext> contexts, FoundgineEfModel model) : IExecutionProvider
{
    public async Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan, ExecutionContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await new EfCoreExecutionProvider(db, model.Registry).ExecuteAsync(plan, context, cancellationToken);
    }
}
