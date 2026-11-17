using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Providers.Security.Warrants.Postgres;
using Foundgine.Providers.Redis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Xunit;

namespace Foundgine.Redis.Tests;

/// <summary>Live PostgreSQL tests. Set FOUNDGINE_POSTGRES_CONNECTION_STRING to enable them.</summary>
public sealed class PostgresSecurityWarrantRevocationEpochAnchorTests
{
    private static NpgsqlDataSource DataSource()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run PostgreSQL anchor integration tests.");
        return NpgsqlDataSource.Create(connectionString);
    }

    [PostgreSqlFact]
    public void Migration_is_idempotent_and_anchor_never_moves_backwards()
    {
        using var dataSource = DataSource();
        PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource);
        PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource);
        var anchor = new PostgresSecurityWarrantRevocationEpochAnchor(dataSource);
        var baseline = anchor.ReadHighWaterMark();

        anchor.AdvanceHighWaterMark(baseline + 10);
        anchor.AdvanceHighWaterMark(baseline + 3);

        Assert.Equal(baseline + 10, anchor.ReadHighWaterMark());
    }

    [PostgreSqlFact]
    public async Task Concurrent_hosts_atomically_preserve_the_maximum_sequence()
    {
        using var dataSource = DataSource();
        PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource);
        var anchor = new PostgresSecurityWarrantRevocationEpochAnchor(dataSource);
        var baseline = anchor.ReadHighWaterMark();
        var updates = Enumerable.Range(1, 64)
            .Select(i => Task.Run(() => new PostgresSecurityWarrantRevocationEpochAnchor(dataSource)
                .AdvanceHighWaterMark(baseline + i)))
            .ToArray();

        await Task.WhenAll(updates);

        Assert.Equal(baseline + 64, anchor.ReadHighWaterMark());
    }

    [PostgreSqlFact]
    public void Negative_sequences_are_rejected_without_changing_the_anchor()
    {
        using var dataSource = DataSource();
        PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource);
        var anchor = new PostgresSecurityWarrantRevocationEpochAnchor(dataSource);
        var before = anchor.ReadHighWaterMark();

        Assert.Throws<ArgumentOutOfRangeException>(() => anchor.AdvanceHighWaterMark(-1));
        Assert.Equal(before, anchor.ReadHighWaterMark());
    }

    [PostgreSqlFact]
    public void Simulated_redis_rollback_is_rejected_then_restored_sequence_is_accepted()
    {
        using var dataSource = DataSource();
        PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(dataSource);
        var anchor = new PostgresSecurityWarrantRevocationEpochAnchor(dataSource);
        var durableBefore = anchor.ReadHighWaterMark();
        anchor.AdvanceHighWaterMark(durableBefore + 2);

        var rolledBackRedis = new AnchoredSecurityWarrantRevocationStore(
            new FixedSequenceRevocationStore(durableBefore + 1), anchor);
        Assert.Throws<InvalidOperationException>(() => _ = rolledBackRedis.CurrentSequence);

        var recoveredRedis = new AnchoredSecurityWarrantRevocationStore(
            new FixedSequenceRevocationStore(durableBefore + 2), anchor);
        Assert.Equal(durableBefore + 2, recoveredRedis.CurrentSequence);
    }

    [PostgreSqlFact]
    public async Task Readiness_check_reports_database_outage_as_unhealthy()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")!)
        {
            Host = "127.0.0.1",
            Port = 1,
            Timeout = 1
        };
        using var unavailable = NpgsqlDataSource.Create(builder.ConnectionString);
        var check = new PostgresSecurityWarrantRevocationEpochAnchorHealthCheck(
            new PostgresSecurityWarrantRevocationEpochAnchor(unavailable));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private sealed class FixedSequenceRevocationStore(long sequence) : ISecurityWarrantRevocationStore
    {
        public long CurrentSequence => sequence;
        public SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now) =>
            throw new NotSupportedException("This test only supplies a Redis sequence observation.");
        public bool IsRevoked(string warrantId, string warrantDigest) => false;
        public bool IsDigestRevoked(string warrantDigest) => false;
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")))
            Skip = "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run this PostgreSQL integration test.";
    }
}
