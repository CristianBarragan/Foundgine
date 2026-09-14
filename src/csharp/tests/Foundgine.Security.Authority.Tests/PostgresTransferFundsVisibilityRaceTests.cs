using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres;
using Foundgine.HighAssurance.Postgres.Execution;
using Foundgine.Core.Semantic.Authorization;
using Npgsql;
using System.Data;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>
/// Verifies that execution-time authorization and account state are read from the
/// post-lock PostgreSQL view, rather than from a stale pre-lock observation.
/// </summary>
public sealed class PostgresTransferFundsVisibilityRaceTests
{
    [PostgresFact]
    public async Task Authorization_context_revoked_while_transfer_waits_is_evaluated_after_lock_acquisition()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 113;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);

        await using var blocker = await dataSource.OpenConnectionAsync();
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockAccountsAsync(blocker, blockerTx, source, destination);

        // This represents authorization state external to the account rows. The transfer
        // must not snapshot it before waiting for the authoritative account locks.
        var authorizationRevoked = false;
        var authorizationObservedAfterRevocation = false;
        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                (_, _, _) =>
                {
                    if (authorizationRevoked)
                        authorizationObservedAfterRevocation = true;
                    return !authorizationRevoked;
                }));

        var transferTask = service.ExecuteAsync(
            actor,
            tenant,
            new TransferFundsCommand(source, destination, 100m, "visibility-authorization-context-race"));

        await Task.Delay(100);
        Assert.False(transferTask.IsCompleted, "Transfer should wait for the account row locks.");

        // Revoke authorization while the transfer is blocked. A correct implementation
        // invokes the authorization decision only after the row locks are acquired.
        authorizationRevoked = true;
        await blockerTx.CommitAsync();

        await Assert.ThrowsAsync<SemanticAuthorizationException>(() => transferTask);
        Assert.True(authorizationObservedAfterRevocation,
            "Authorization must be evaluated after the transfer acquires its authoritative locks.");

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.SourceBalance);
        Assert.Equal(1000m, state.DestinationBalance);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }

    [PostgresFact]
    public async Task Ownership_change_queued_before_transfer_lock_is_visible_after_lock_acquisition()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 108;
        var originalActor = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, originalActor, source, destination);

        await using var blocker = await dataSource.OpenConnectionAsync();
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockAccountsAsync(blocker, blockerTx, source, destination);

        // Queue the ownership change behind the blocker before the transfer attempts
        // to acquire the same rows. PostgreSQL lock ordering then makes the visibility
        // transition deterministic: owner change commits before the transfer acquires
        // its FOR UPDATE locks.
        await using var ownershipConnection = await dataSource.OpenConnectionAsync();
        await using var ownershipTx = await ownershipConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var ownershipTask = ChangeOwnerAsync(ownershipConnection, ownershipTx, source, newOwner);
        await Task.Delay(100);

        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                static (actor, sourceAccount, destinationAccount) =>
                    actor == sourceAccount.OwnerId && actor == destinationAccount.OwnerId));

        var transferTask = service.ExecuteAsync(
            originalActor,
            tenant,
            new TransferFundsCommand(source, destination, 100m, "visibility-race"));

        await Task.Delay(100);
        Assert.False(transferTask.IsCompleted, "Transfer should wait for the account row locks.");

        await blockerTx.CommitAsync();
        await ownershipTask;
        await ownershipTx.CommitAsync();

        await Assert.ThrowsAsync<SemanticAuthorizationException>(() => transferTask);

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.SourceBalance);
        Assert.Equal(1000m, state.DestinationBalance);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);

        var owner = await ReadOwnerAsync(dataSource, source);
        Assert.Equal(newOwner, owner);
    }

    [PostgresFact]
    public async Task Frozen_state_committed_before_lock_acquisition_is_observed_and_blocks_transfer()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 109;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);

        await using var blocker = await dataSource.OpenConnectionAsync();
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockAccountsAsync(blocker, blockerTx, source, destination);

        await using var stateConnection = await dataSource.OpenConnectionAsync();
        await using var stateTx = await stateConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var freezeTask = SetFrozenAsync(stateConnection, stateTx, destination);
        await Task.Delay(100);

        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                static (id, a, b) => id == a.OwnerId && id == b.OwnerId));

        var transferTask = service.ExecuteAsync(
            actor,
            tenant,
            new TransferFundsCommand(source, destination, 100m, "visibility-frozen-race"));

        await Task.Delay(100);
        Assert.False(transferTask.IsCompleted);

        await blockerTx.CommitAsync();
        await freezeTask;
        await stateTx.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transferTask);

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.SourceBalance);
        Assert.Equal(1000m, state.DestinationBalance);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
    }


    [PostgresFact]
    public async Task
        Tenant_reassignment_committed_before_lock_acquisition_is_observed_and_blocks_cross_tenant_transfer()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var originalTenant = 110;
        var reassignedTenant = 111;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, originalTenant, actor, source, destination);

        await using var blocker = await dataSource.OpenConnectionAsync();
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockAccountsAsync(blocker, blockerTx, source, destination);

        await using var tenantConnection = await dataSource.OpenConnectionAsync();
        await using var tenantTx = await tenantConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var reassignmentTask = ChangeTenantAsync(tenantConnection, tenantTx, source, reassignedTenant);
        await Task.Delay(100);

        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                static (id, a, b) => id == a.OwnerId && id == b.OwnerId));

        var transferTask = service.ExecuteAsync(
            actor,
            originalTenant,
            new TransferFundsCommand(source, destination, 100m, "visibility-tenant-race"));

        await Task.Delay(100);
        Assert.False(transferTask.IsCompleted, "Transfer should wait for the account row locks.");

        await blockerTx.CommitAsync();
        await reassignmentTask;
        await tenantTx.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transferTask);

        var state = await ReadStateAsync(dataSource, source, destination);
        Assert.Equal(1000m, state.SourceBalance);
        Assert.Equal(1000m, state.DestinationBalance);
        Assert.Equal(0, state.IdempotencyCount);
        Assert.Equal(0, state.AuditCount);
        Assert.Equal(reassignedTenant, await ReadTenantAsync(dataSource, source));
    }

    [PostgresFact]
    public async Task
        Account_deleted_before_lock_acquisition_is_observed_and_transfer_cannot_cross_missing_account_boundary()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);

        var tenant = 112;
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        await SeedAsync(dataSource, tenant, actor, source, destination);

        await using var blocker = await dataSource.OpenConnectionAsync();
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockAccountsAsync(blocker, blockerTx, source, destination);

        await using var deleteConnection = await dataSource.OpenConnectionAsync();
        await using var deleteTx = await deleteConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var deleteTask = DeleteAccountAsync(deleteConnection, deleteTx, source);
        await Task.Delay(100);

        var service = new PostgresTransferFundsService(
            new PostgresTransferFundsExecutor(
                dataSource,
                static (id, a, b) => id == a.OwnerId && id == b.OwnerId));

        var transferTask = service.ExecuteAsync(
            actor,
            tenant,
            new TransferFundsCommand(source, destination, 100m, "visibility-delete-race"));

        await Task.Delay(100);
        Assert.False(transferTask.IsCompleted, "Transfer should wait for the account row locks.");

        await blockerTx.CommitAsync();
        await deleteTask;
        await deleteTx.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transferTask);

        var destinationBalance = await ReadBalanceAsync(dataSource, destination);
        Assert.Equal(1000m, destinationBalance);
        Assert.Equal(0, await ReadCountAsync(dataSource, "banking.transfer_idempotency"));
        Assert.Equal(0, await ReadCountAsync(dataSource, "banking.transfer_audit"));
        Assert.False(await AccountExistsAsync(dataSource, source));
    }

    private static async Task ChangeTenantAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid accountId,
        int tenant)
    {
        await using var command =
            new NpgsqlCommand("UPDATE banking.bank_account SET tenant_id = @tenant WHERE id = @id;", connection, tx);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", accountId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteAccountAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid accountId)
    {
        await using var command = new NpgsqlCommand("DELETE FROM banking.bank_account WHERE id = @id;", connection, tx);
        command.Parameters.AddWithValue("id", accountId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task LockAccountsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid source,
        Guid destination)
    {
        await using var command =
            new NpgsqlCommand("SELECT id FROM banking.bank_account WHERE id = ANY(@ids) ORDER BY id FOR UPDATE;",
                connection, tx);
        command.Parameters.AddWithValue("ids", new[] { source, destination });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
        }
    }

    private static async Task ChangeOwnerAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid accountId,
        Guid newOwner)
    {
        await using var command = new NpgsqlCommand("UPDATE banking.bank_account SET owner_id = @owner WHERE id = @id;",
            connection, tx);
        command.Parameters.AddWithValue("owner", newOwner);
        command.Parameters.AddWithValue("id", accountId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetFrozenAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid accountId)
    {
        await using var command = new NpgsqlCommand("UPDATE banking.bank_account SET is_frozen = true WHERE id = @id;",
            connection, tx);
        command.Parameters.AddWithValue("id", accountId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PrepareAsync(NpgsqlDataSource dataSource)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
        await using var clear =
            dataSource.CreateCommand(
                "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.bank_account;");
        await clear.ExecuteNonQueryAsync();
    }

    private static async Task SeedAsync(NpgsqlDataSource dataSource, int tenant, Guid actor, Guid source,
        Guid destination)
    {
        const string sql = """
                           INSERT INTO banking.bank_account(id, tenant_id, owner_id, balance, pending_transactions, regulatory_hold, daily_transferred, daily_limit, is_frozen)
                           VALUES (@source, @tenant, @actor, 1000, 0, 0, 0, 1000000, false),
                                  (@destination, @tenant, @actor, 1000, 0, 0, 0, 1000000, false);
                           """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> ReadOwnerAsync(NpgsqlDataSource dataSource, Guid accountId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command =
            new NpgsqlCommand("SELECT owner_id FROM banking.bank_account WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", accountId);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<State> ReadStateAsync(NpgsqlDataSource dataSource, Guid source, Guid destination)
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
        return new State(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetInt64(2), reader.GetInt64(3));
    }


    private static async Task<int> ReadTenantAsync(NpgsqlDataSource dataSource, Guid accountId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command =
            new NpgsqlCommand("SELECT tenant_id FROM banking.bank_account WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", accountId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<decimal> ReadBalanceAsync(NpgsqlDataSource dataSource, Guid accountId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command =
            new NpgsqlCommand("SELECT balance FROM banking.bank_account WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", accountId);
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> ReadCountAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table};", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> AccountExistsAsync(NpgsqlDataSource dataSource, Guid accountId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command =
            new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM banking.bank_account WHERE id = @id);", connection);
        command.Parameters.AddWithValue("id", accountId);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private sealed record State(
        decimal SourceBalance,
        decimal DestinationBalance,
        long IdempotencyCount,
        long AuditCount);
}