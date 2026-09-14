using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using System.Data;
using Foundgine.Runtime.ControlPlane;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>
/// : authorization lifecycle writes require registered provenance,
/// tenant/actor scope, database-role binding, and monotonic writer sequencing.
/// </summary>
public sealed class PostgresAuthorizationContextProvenanceSecurityTests
{
    [PostgresFact]
    public async Task Cross_tenant_writer_cannot_write_an_authorization_context()
    {
        await using var ds = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(ds);
        var actor = Guid.NewGuid();
        var writerActor = Guid.NewGuid();
        var writer = await RegisterWriterAsync(ds, writerActor, 601, "secret-1");
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());

        await using var c = await ds.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.CreateAsync(c, tx, actor, 602, true, 1, "auth-v1", writer with { ActorId = actor, TenantId = 602 }));
        await tx.RollbackAsync();
    }

    [PostgresFact]
    public async Task Actor_impersonation_is_rejected_even_when_tenant_matches()
    {
        await using var ds = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(ds);
        var registeredActor = Guid.NewGuid();
        var targetActor = Guid.NewGuid();
        var writer = await RegisterWriterAsync(ds, registeredActor, 603, "secret-2");
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());

        await using var c = await ds.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.CreateAsync(c, tx, targetActor, 603, true, 1, "auth-v1", writer with { ActorId = targetActor }));
        await tx.RollbackAsync();
    }

    [PostgresFact]
    public async Task Inactive_writer_and_forged_credential_are_rejected()
    {
        await using var ds = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(ds);
        var actor = Guid.NewGuid();
        var writer = await RegisterWriterAsync(ds, actor, 604, "secret-3");
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());

        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await DeactivateWriterAsync(c, tx, writer.WriterId);
            await tx.CommitAsync();
        }

        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store.CreateAsync(c, tx, actor, 604, true, 1, "auth-v1", writer));
            await tx.RollbackAsync();
        }

        var activeWriter = await RegisterWriterAsync(ds, actor, 605, "secret-4");
        await using var c2 = await ds.OpenConnectionAsync();
        await using var tx2 = await c2.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.CreateAsync(c2, tx2, actor, 605, true, 1, "auth-v1",
                activeWriter with { CredentialFingerprint = "forged" }));
        await tx2.RollbackAsync();
    }

    [PostgresFact]
    public async Task Stale_writer_sequence_is_rejected_and_concurrent_writer_sequence_is_serialized()
    {
        await using var ds = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(ds);
        var actor = Guid.NewGuid();
        var writer = await RegisterWriterAsync(ds, actor, 606, "secret-5");
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());

        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await store.CreateAsync(c, tx, actor, 606, true, 1, "auth-v1", writer with { WriteSequence = 1 });
            await tx.CommitAsync();
        }

        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.UpdateAsync(c, tx, actor, 606, false, 2, "auth-v2", writer with { WriteSequence = 1 }));
            await tx.RollbackAsync();
        }

        await using var lockConnection = await ds.OpenConnectionAsync();
        await using var lockTx = await lockConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using (var lockCommand =
                     new NpgsqlCommand(
                         "SELECT writer_id FROM banking.authorization_context_writer WHERE writer_id=@writer FOR UPDATE;",
                         lockConnection, lockTx))
        {
            lockCommand.Parameters.AddWithValue("writer", writer.WriterId);
            Assert.Equal(writer.WriterId, (Guid)(await lockCommand.ExecuteScalarAsync())!);
        }

        var blocked = Task.Run(async () =>
        {
            await using var c = await ds.OpenConnectionAsync();
            await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.UpdateAsync(c, tx, actor, 606, false, 2, "auth-v2", writer with { WriteSequence = 1 }));
            await tx.RollbackAsync();
        });
        await Task.Delay(100);
        Assert.False(blocked.IsCompleted);
        await lockTx.CommitAsync();
        await blocked;
    }

    private static async Task<AuthorizationWriteProvenance> RegisterWriterAsync(NpgsqlDataSource ds, Guid actor,
        int tenant, string credential)
    {
        var writer = Guid.NewGuid();
        await using var c = await ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
                                                INSERT INTO banking.authorization_context_writer(writer_id, actor_id, tenant_id, active, database_role, credential_fingerprint)
                                                VALUES (@writer, @actor, @tenant, true, current_user, @credential);
                                                """, c);
        cmd.Parameters.AddWithValue("writer", writer);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("credential", credential);
        await cmd.ExecuteNonQueryAsync();
        return new AuthorizationWriteProvenance(writer, actor, tenant, 1, credential);
    }

    private static async Task DeactivateWriterAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid writer)
    {
        await using var cmd =
            new NpgsqlCommand("UPDATE banking.authorization_context_writer SET active=false WHERE writer_id=@writer;",
                c, tx);
        cmd.Parameters.AddWithValue("writer", writer);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task PrepareAsync(NpgsqlDataSource ds)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = ds.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
        await using var clear =
            ds.CreateCommand(
                "TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.authorization_context, banking.authorization_context_tombstone, banking.authorization_context_writer, banking.bank_account;");
        await clear.ExecuteNonQueryAsync();
    }
}

internal static class TestAuthorizationIntegrity
{
    public static AuthorizationContextIntegrityKeyRing CreateKeyRing() =>
        new(new AuthorizationContextIntegrityKey("test-key-v1",
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("Foundgine--test-integrity-key"))));
}