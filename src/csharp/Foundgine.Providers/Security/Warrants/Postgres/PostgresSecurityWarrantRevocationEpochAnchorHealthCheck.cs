using Foundgine.Core.Semantic.Security.Warrants;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Foundgine.Providers.Security.Warrants.Postgres;

/// <summary>Readiness check that verifies the durable epoch anchor can be read and monotonically reaffirmed.</summary>
public sealed class PostgresSecurityWarrantRevocationEpochAnchorHealthCheck(
    ISecurityWarrantRevocationEpochAnchor anchor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var value = anchor.ReadHighWaterMark();
            anchor.AdvanceHighWaterMark(value);
            return Task.FromResult(HealthCheckResult.Healthy(
                "PostgreSQL warrant revocation epoch anchor is readable and writable.",
                new Dictionary<string, object> { ["highWaterMark"] = value }));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "PostgreSQL warrant revocation epoch anchor is unavailable; warrant authorization must fail closed.", exception));
        }
    }
}
