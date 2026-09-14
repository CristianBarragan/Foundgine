using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres;
using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>
/// proves that authorization decisions carry explicit versioned evidence
/// and that the evidence must still match immediately before commit.
/// </summary>
public sealed class PostgresTransferFundsAuthorizationEvidenceTests
{
    [PostgresFact]
    public async Task Stale_authorization_evidence_is_rejected_before_mutation()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 114;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);

        var authorizationVersion = 7L;
        var decisionCount = 0;

        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                (_, _, _) =>
                {
                    var version = authorizationVersion;
                    if (Interlocked.Increment(ref decisionCount) == 1)
                        authorizationVersion = 8L;

                    return new AuthorizationDecision(
                        true,
                        version,
                        $"authorization-context-v{version}");
                }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(
                actor,
                tenant,
                new TransferFundsCommand(source, destination, 100m, "authorization-evidence-stale")));

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.Source);
        Assert.Equal(1000m, state.Destination);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Authorization_context_change_after_mutation_before_commit_is_rejected()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 115;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);

        var authorizationVersion = 21L;
        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                (_, _, _) =>
                {
                    var version = authorizationVersion;
                    return new AuthorizationDecision(
                        true,
                        version,
                        $"authorization-context-v{version}");
                },
                point =>
                {
                    if (point == PostgresTransferFundsFaultPoint.BeforeAuthorizationCommitCheck)
                        authorizationVersion = 22L;
                }));

        var transferTask = service.ExecuteAsync(
            actor,
            tenant,
            new TransferFundsCommand(source, destination, 100m, "authorization-evidence-commit-gate"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => transferTask);

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.Source);
        Assert.Equal(1000m, state.Destination);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Batch_commit_gate_rejects_changed_authorization_evidence_and_rolls_back_all_transfers()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 116;
        var actor = Guid.NewGuid();
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var destinationA = Guid.NewGuid();
        var destinationB = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, sourceA, destinationA);
        await SeedAsync(dataSource, tenant, actor, sourceB, destinationB);

        var authorizationVersion = 31L;
        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                (_, _, _) =>
                {
                    var version = authorizationVersion;
                    return new AuthorizationDecision(
                        true,
                        version,
                        $"authorization-context-v{version}");
                },
                point =>
                {
                    if (point == PostgresTransferFundsFaultPoint.BeforeBatchAuthorizationCommitCheck)
                        authorizationVersion = 32L;
                }));

        var commands = new[]
        {
            new TransferFundsCommand(sourceA, destinationA, 100m, "authorization-batch-a"),
            new TransferFundsCommand(sourceB, destinationB, 200m, "authorization-batch-b")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteBatchAsync(actor, tenant, commands));

        var first = await ReadBalancesAsync(dataSource, sourceA, destinationA);
        var second = await ReadBalancesAsync(dataSource, sourceB, destinationB);

        Assert.Equal(1000m, first.Source);
        Assert.Equal(1000m, first.Destination);
        Assert.Equal(1000m, second.Source);
        Assert.Equal(1000m, second.Destination);
        Assert.Equal(0, await ReadCountAsync(dataSource, "banking.transfer_idempotency"));
        Assert.Equal(0, await ReadCountAsync(dataSource, "banking.transfer_audit"));
    }

    private static async Task PrepareAsync(NpgsqlDataSource dataSource)
    {
        var sql = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
        await using var clear = dataSource.CreateCommand(
            "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.bank_account;");
        await clear.ExecuteNonQueryAsync();
    }

    private static async Task SeedAsync(
        NpgsqlDataSource dataSource,
        int tenant,
        Guid actor,
        Guid source,
        Guid destination)
    {
        const string sql = """
                           INSERT INTO banking.bank_account
                           (id, tenant_id, owner_id, balance, pending_transactions,
                           regulatory_hold, daily_transferred, daily_limit, is_frozen)
                           VALUES
                           (@source, @tenant, @actor, 1000, 0, 0, 0, 1000000, false),
                           (@destination, @tenant, @actor, 1000, 0, 0, 0, 1000000, false);
                           """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(decimal Source, decimal Destination)> ReadBalancesAsync(
        NpgsqlDataSource dataSource,
        Guid source,
        Guid destination)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        const string sql = """
                           SELECT
                           (SELECT balance FROM banking.bank_account WHERE id = @source),
                           (SELECT balance FROM banking.bank_account WHERE id = @destination);
                           """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("destination", destination);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private static async Task<(decimal Source, decimal Destination, long IdempotencyCount, long AuditCount)>
        ReadStateAsync(
            NpgsqlDataSource dataSource,
            Guid source,
            Guid destination)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        const string sql = """
                           SELECT
                           (SELECT balance FROM banking.bank_account WHERE id = @source),
                           (SELECT balance FROM banking.bank_account WHERE id = @destination),
                           (SELECT count(*) FROM banking.transfer_idempotency),
                           (SELECT count(*) FROM banking.transfer_audit);
                           """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("destination", destination);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static async Task<long> ReadCountAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {table};",
            connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}