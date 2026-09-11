using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using System.Data;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>
/// closes authorization-context lifecycle races and replay paths.
/// The authorization identity is (actor, tenant), its version is monotonic,
/// and absence is security-negative for mutation execution.
/// </summary>
public sealed class PostgresAuthorizationContextLifecycleSecurityTests
{
    [PostgresFact]
    public async Task Version_must_increase_and_fingerprint_must_be_present()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        var actor = Guid.NewGuid();
        const int tenant = 501;
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());
        var writer = await RegisterWriterAsync(dataSource, actor, tenant, "lifecycle-1");

        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await store.CreateAsync(connection, tx, actor, tenant, true, 1, "auth-v1",
                writer with { WriteSequence = 1 });
            await tx.CommitAsync();
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.UpdateAsync(connection, tx, actor, tenant, true, 2, "auth-replay",
                    writer with { WriteSequence = 1 }));
            await tx.RollbackAsync();
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.UpdateAsync(connection, tx, actor, tenant, true, 2, " ", writer with { WriteSequence = 2 }));
            await tx.RollbackAsync();
        }
    }

    [PostgresFact]
    public async Task Identity_reassignment_is_not_an_update_of_an_existing_authorization_context()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        var actorA = Guid.NewGuid();
        var actorB = Guid.NewGuid();
        const int tenant = 502;
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());
        var writerA = await RegisterWriterAsync(dataSource, actorA, tenant, "lifecycle-a");
        var writerB = await RegisterWriterAsync(dataSource, actorB, tenant, "lifecycle-b");

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await store.CreateAsync(connection, tx, actorA, tenant, true, 1, "actor-a-v1",
            writerA with { WriteSequence = 1 });
        await store.CreateAsync(connection, tx, actorB, tenant, true, 1, "actor-b-v1",
            writerB with { WriteSequence = 1 });
        await tx.CommitAsync();

        await using var verify = await dataSource.OpenConnectionAsync();
        await using var command =
            new NpgsqlCommand("SELECT count(*) FROM banking.authorization_context WHERE tenant_id = @tenant;", verify);
        command.Parameters.AddWithValue("tenant", tenant);
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [PostgresFact]
    public async Task Deletion_is_serialized_by_the_same_authoritative_row_lock()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        var actor = Guid.NewGuid();
        const int tenant = 503;
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());
        var writer = await RegisterWriterAsync(dataSource, actor, tenant, "lifecycle-3");

        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await store.CreateAsync(connection, tx, actor, tenant, true, 1, "auth-v1",
                writer with { WriteSequence = 1 });
            await tx.CommitAsync();
        }

        await using var lockConnection = await dataSource.OpenConnectionAsync();
        await using var lockTx = await lockConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var locked = await store.LoadForUpdateAsync(lockConnection, lockTx, actor, tenant);
        Assert.NotNull(locked);

        var deleteTask = Task.Run(async () =>
        {
            await using var deleteConnection = await dataSource.OpenConnectionAsync();
            await using var deleteTx = await deleteConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await store.DeleteAsync(deleteConnection, deleteTx, actor, tenant, writer with { WriteSequence = 2 });
            await deleteTx.CommitAsync();
        });

        await Task.Delay(100);
        Assert.False(deleteTask.IsCompleted);

        await lockTx.CommitAsync();
        await deleteTask;

        await using var verify = await dataSource.OpenConnectionAsync();
        await using var command =
            new NpgsqlCommand(
                "SELECT count(*) FROM banking.authorization_context WHERE actor_id = @actor AND tenant_id = @tenant;",
                verify);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("tenant", tenant);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [PostgresFact]
    public async Task Recreated_identity_starts_a_new_explicit_lifecycle_and_does_not_replay_the_old_version()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        var actor = Guid.NewGuid();
        const int tenant = 504;
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());
        var writer = await RegisterWriterAsync(dataSource, actor, tenant, "lifecycle-4");

        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await store.CreateAsync(connection, tx, actor, tenant, true, 7, "auth-v7",
                writer with { WriteSequence = 1 });
            await store.DeleteAsync(connection, tx, actor, tenant, writer with { WriteSequence = 2 });
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.CreateAsync(connection, tx, actor, tenant, true, 7, "auth-replay-v7",
                    writer with { WriteSequence = 3 }));
            await store.CreateAsync(connection, tx, actor, tenant, true, 8, "auth-new-lifecycle-v8",
                writer with { WriteSequence = 4 });
            await tx.CommitAsync();
        }

        await using var verifyConnection = await dataSource.OpenConnectionAsync();
        await using var verifyTx = await verifyConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var context = await store.LoadForUpdateAsync(verifyConnection, verifyTx, actor, tenant);
        Assert.NotNull(context);
        Assert.Equal(8, context.Version);
        Assert.Equal("auth-new-lifecycle-v8", context.Fingerprint);
        await verifyTx.RollbackAsync();
    }

    private static async Task<AuthorizationWriteProvenance> RegisterWriterAsync(NpgsqlDataSource ds, Guid actor,
        int tenant, string credential)
    {
        var writer = Guid.NewGuid();
        await using var connection = await ds.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
                                                    INSERT INTO banking.authorization_context_writer(writer_id, actor_id, tenant_id, active, database_role, credential_fingerprint)
                                                    VALUES (@writer, @actor, @tenant, true, current_user, @credential);
                                                    """, connection);
        command.Parameters.AddWithValue("writer", writer);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("credential", credential);
        await command.ExecuteNonQueryAsync();
        return new AuthorizationWriteProvenance(writer, actor, tenant, 1, credential);
    }

    private static async Task PrepareAsync(NpgsqlDataSource dataSource)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
        await using var clear = dataSource.CreateCommand(
            "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.authorization_context, banking.authorization_context_tombstone, banking.authorization_context_writer, banking.bank_account;");
        await clear.ExecuteNonQueryAsync();
    }
}