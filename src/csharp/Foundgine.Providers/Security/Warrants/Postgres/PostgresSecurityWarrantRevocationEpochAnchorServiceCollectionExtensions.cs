using Foundgine.Core.Semantic.Security.Warrants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Foundgine.Providers.Security.Warrants.Postgres;

public static class PostgresSecurityWarrantRevocationEpochAnchorServiceCollectionExtensions
{
    /// <summary>Registers the PostgreSQL durable anchor. The caller must run its migrator before accepting traffic.</summary>
    public static IServiceCollection AddFoundginePostgresWarrantRevocationEpochAnchor(
        this IServiceCollection services, NpgsqlDataSource dataSource, bool addReadinessCheck = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);
        services.TryAddSingleton<PostgresSecurityWarrantRevocationEpochAnchor>(
            _ => new PostgresSecurityWarrantRevocationEpochAnchor(dataSource));
        services.TryAddSingleton<ISecurityWarrantRevocationEpochAnchor>(
            sp => sp.GetRequiredService<PostgresSecurityWarrantRevocationEpochAnchor>());
        if (addReadinessCheck)
            services.AddHealthChecks().AddCheck<PostgresSecurityWarrantRevocationEpochAnchorHealthCheck>(
                "foundgine-warrant-revocation-epoch-anchor", failureStatus: HealthStatus.Unhealthy, tags: ["ready", "security"]);
        return services;
    }
}
