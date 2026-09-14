using Foundgine.Runtime.ControlPlane;
using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres;
using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>
/// proves that authorization evidence is bound to the PostgreSQL
/// transaction's serialization point rather than merely checked twice.
/// </summary>
public sealed class PostgresTransferFundsAuthorizationAtomicityTests
{
    [PostgresFact]
    public async Task Authorization_revocation_committed_before_lock_is_observed_and_rejected()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 117;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);
        await SeedAuthorizationAsync(dataSource, actor, tenant, true, 1, "auth-v1");

        // A concurrent authorization writer commits first. The transfer must
        // serialize behind that committed state and cannot use v1.
        await UpdateAuthorizationAsync(dataSource, actor, tenant, false, 2, "auth-v2");

        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                (_, _, _) => new AuthorizationDecision(true, 1, "auth-v1"),
                authorizationContextStore: new PostgresAuthorizationContextStore(TestAuthorizationIntegrity
                    .CreateKeyRing())));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(
                actor,
                tenant,
                new TransferFundsCommand(source, destination, 100m, "authorization-atomic-precommit")));

        var balances = await ReadBalancesAsync(dataSource, source, destination);
        Assert.Equal(1000m, balances.Source);
        Assert.Equal(1000m, balances.Destination);
        Assert.Equal(0, await ReadCountAsync(dataSource, "banking.transfer_idempotency"));
        Assert.Equal(0, await ReadCountAsync(dataSource, "banking.transfer_audit"));
    }

    [PostgresFact]
    public async Task Authorization_change_attempted_after_context_lock_is_serialized_after_commit()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 118;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);
        await SeedAuthorizationAsync(dataSource, actor, tenant, true, 10, "auth-v10");

        var revocationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? revocationTask = null;

        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                (_, _, _) => new AuthorizationDecision(true, 10, "auth-v10"),
                point =>
                {
                    if (point == PostgresTransferFundsFaultPoint.BeforeAuthorizationCommitCheck)
                    {
                        revocationTask = Task.Run(async () =>
                        {
                            revocationStarted.TrySetResult();
                            await UpdateAuthorizationAsync(dataSource, actor, tenant, false, 11, "auth-v11");
                        });

                        // Give the competing writer time to reach the locked row.
                        // It cannot commit until this mutation transaction releases it.
                        revocationStarted.Task.GetAwaiter().GetResult();
                        Task.Delay(100).GetAwaiter().GetResult();
                    }
                },
                authorizationContextStore: new PostgresAuthorizationContextStore(TestAuthorizationIntegrity
                    .CreateKeyRing())));

        var receipt = await service.ExecuteAsync(
            actor,
            tenant,
            new TransferFundsCommand(source, destination, 100m, "authorization-atomic-commit"));

        Assert.False(receipt.Replay);
        Assert.NotNull(revocationTask);
        await revocationTask!;

        // The mutation linearizes before the revocation. The revocation then
        // commits as a separate transaction, producing a clean serialization.
        var balances = await ReadBalancesAsync(dataSource, source, destination);
        Assert.Equal(900m, balances.Source);
        Assert.Equal(1100m, balances.Destination);

        var context = await ReadAuthorizationAsync(dataSource, actor, tenant);
        Assert.False(context.Allowed);
        Assert.Equal(11, context.Version);
        Assert.Equal("auth-v11", context.Fingerprint);
    }

    [PostgresFact]
    public async Task Batch_authorization_context_is_locked_once_and_bound_to_batch_commit()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 119;
        var actor = Guid.NewGuid();
        var sourceA = Guid.NewGuid();
        var destinationA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();
        var destinationB = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, sourceA, destinationA);
        await SeedAsync(dataSource, tenant, actor, sourceB, destinationB);
        await SeedAuthorizationAsync(dataSource, actor, tenant, true, 30, "auth-v30");

        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                (_, _, _) => new AuthorizationDecision(true, 30, "auth-v30"),
                authorizationContextStore: new PostgresAuthorizationContextStore(TestAuthorizationIntegrity
                    .CreateKeyRing())));

        var receipts = await service.ExecuteBatchAsync(
            actor,
            tenant,
            [
                new TransferFundsCommand(sourceA, destinationA, 100m, "authorization-atomic-batch-a"),
                new TransferFundsCommand(sourceB, destinationB, 200m, "authorization-atomic-batch-b")
            ]);

        Assert.Equal(2, receipts.Count);
        Assert.All(receipts, receipt => Assert.False(receipt.Replay));
        Assert.Equal(2, await ReadCountAsync(dataSource, "banking.transfer_idempotency"));
        Assert.Equal(2, await ReadCountAsync(dataSource, "banking.transfer_audit"));
    }

    private static async Task PrepareAsync(NpgsqlDataSource dataSource)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
        await using var clear = dataSource.CreateCommand(
            "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.authorization_context, banking.authorization_context_tombstone, banking.bank_account;");
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

    private static async Task SeedAuthorizationAsync(
        NpgsqlDataSource dataSource,
        Guid actor,
        int tenant,
        bool allowed,
        long version,
        string fingerprint) => await UpdateAuthorizationAsync(
        dataSource, actor, tenant, allowed, version, fingerprint);

    private static async Task UpdateAuthorizationAsync(
        NpgsqlDataSource dataSource,
        Guid actor,
        int tenant,
        bool allowed,
        long version,
        string fingerprint)
    {
        const string sql = """
                           INSERT INTO banking.authorization_context(actor_id, tenant_id, allowed, version, fingerprint, integrity_algorithm, integrity_key_id, integrity_tag)
                           VALUES (@actor, @tenant, @allowed, @version, @fingerprint, @algorithm, @key_id, @tag)
                           ON CONFLICT (actor_id, tenant_id) DO UPDATE SET
                           allowed = EXCLUDED.allowed,
                           version = EXCLUDED.version,
                           fingerprint = EXCLUDED.fingerprint,
                           integrity_algorithm = EXCLUDED.integrity_algorithm,
                           integrity_key_id = EXCLUDED.integrity_key_id,
                           integrity_tag = EXCLUDED.integrity_tag;
                           """;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("allowed", allowed);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        var integrity = TestAuthorizationIntegrity.CreateKeyRing();
        command.Parameters.AddWithValue("algorithm", AuthorizationContextIntegrityKeyRing.CurrentAlgorithmVersion);
        command.Parameters.AddWithValue("key_id", integrity.ActiveKeyId);
        command.Parameters.AddWithValue("tag",
            integrity.ComputeContextTag(actor, tenant, allowed, version, fingerprint));
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

    private static async Task<(bool Allowed, long Version, string Fingerprint)> ReadAuthorizationAsync(
        NpgsqlDataSource dataSource,
        Guid actor,
        int tenant)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command =
            new NpgsqlCommand(
                "SELECT allowed, version, fingerprint FROM banking.authorization_context WHERE actor_id = @actor AND tenant_id = @tenant;",
                connection);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("tenant", tenant);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetBoolean(0), reader.GetInt64(1), reader.GetString(2));
    }

    private static async Task<long> ReadCountAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table};", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}