using Foundgine.Runtime.ControlPlane;
using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

public sealed class PostgresAuthorizationRecoverySecurityTests
{
    [PostgresFact]
    public async Task Missing_checkpoint_fails_closed()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var clear =
            new NpgsqlCommand(
                "TRUNCATE banking.authorization_security_recovery_checkpoint, banking.authorization_context_tombstone, banking.authorization_context_writer, banking.authorization_context;",
                connection);
        await clear.ExecuteNonQueryAsync();

        var result = await new PostgresAuthorizationRecoveryCoordinator(dataSource).VerifyAsync();

        Assert.False(result.IsConsistent);
        Assert.Contains("checkpoint", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Tampering_after_checkpoint_fails_closed()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var clear =
            new NpgsqlCommand(
                "TRUNCATE banking.authorization_security_recovery_checkpoint, banking.authorization_context_tombstone, banking.authorization_context_writer, banking.authorization_context;",
                connection);
        await clear.ExecuteNonQueryAsync();

        var actor = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
                         "INSERT INTO banking.authorization_context(actor_id,tenant_id,allowed,version,fingerprint,integrity_algorithm,integrity_key_id,integrity_tag) VALUES (@a,1,true,1,'fp','HMAC-SHA256/v1','key-v1',repeat('0',64));",
                         connection))
        {
            insert.Parameters.AddWithValue("a", actor);
            await insert.ExecuteNonQueryAsync();
        }

        var anchor = new InMemoryAuthorizationRecoverySequenceAnchor();
        var coordinator = new PostgresAuthorizationRecoveryCoordinator(dataSource, anchor);
        await using (var tx = await connection.BeginTransactionAsync())
        {
            await coordinator.SealAsync(connection, tx, 1);
            await tx.CommitAsync();
            await coordinator.AdvanceAnchorAfterCommitAsync(1);
        }

        await using (var tamper =
                     new NpgsqlCommand(
                         "UPDATE banking.authorization_context SET allowed=false WHERE actor_id=@a AND tenant_id=1;",
                         connection))
        {
            tamper.Parameters.AddWithValue("a", actor);
            await tamper.ExecuteNonQueryAsync();
        }

        var result = await coordinator.VerifyAsync();
        Assert.False(result.IsConsistent);
    }

    [PostgresFact]
    public async Task Recovery_accepts_exact_committed_state()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var clear =
            new NpgsqlCommand(
                "TRUNCATE banking.authorization_security_recovery_checkpoint, banking.authorization_context_tombstone, banking.authorization_context_writer, banking.authorization_context;",
                connection);
        await clear.ExecuteNonQueryAsync();

        var anchor = new InMemoryAuthorizationRecoverySequenceAnchor();
        var coordinator = new PostgresAuthorizationRecoveryCoordinator(dataSource, anchor);
        await using (var tx = await connection.BeginTransactionAsync())
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO banking.authorization_context(actor_id,tenant_id,allowed,version,fingerprint,integrity_algorithm,integrity_key_id,integrity_tag) VALUES (@a,1,true,1,'fp','HMAC-SHA256/v1','key-v1',repeat('0',64));",
                connection, tx);
            insert.Parameters.AddWithValue("a", Guid.NewGuid());
            await insert.ExecuteNonQueryAsync();
            await coordinator.SealAsync(connection, tx, 1);
            await tx.CommitAsync();
            await coordinator.AdvanceAnchorAfterCommitAsync(1);
        }

        var result = await coordinator.VerifyAsync();
        Assert.True(result.IsConsistent);
        Assert.Equal(1, result.Sequence);
    }

    [PostgresFact]
    public async Task Older_checkpoint_is_rejected_by_monotonic_anchor()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var clear =
            new NpgsqlCommand(
                "TRUNCATE banking.authorization_security_recovery_checkpoint, banking.authorization_context_tombstone, banking.authorization_context_writer, banking.authorization_context;",
                connection);
        await clear.ExecuteNonQueryAsync();

        var anchor = new InMemoryAuthorizationRecoverySequenceAnchor();
        var coordinator = new PostgresAuthorizationRecoveryCoordinator(dataSource, anchor);
        await using (var tx = await connection.BeginTransactionAsync())
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO banking.authorization_context(actor_id,tenant_id,allowed,version,fingerprint,integrity_algorithm,integrity_key_id,integrity_tag) VALUES (@a,1,true,1,'fp','HMAC-SHA256/v1','key-v1',repeat('0',64));",
                connection, tx);
            insert.Parameters.AddWithValue("a", Guid.NewGuid());
            await insert.ExecuteNonQueryAsync();
            await coordinator.SealAsync(connection, tx, 2);
            await tx.CommitAsync();
            await coordinator.AdvanceAnchorAfterCommitAsync(2);
        }

        await using var rollback =
            new NpgsqlCommand("UPDATE banking.authorization_security_recovery_checkpoint SET sequence=1;", connection);
        await rollback.ExecuteNonQueryAsync();
        var result = await coordinator.VerifyAsync();
        Assert.False(result.IsConsistent);
        Assert.Contains("rollback", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Future_checkpoint_ahead_of_anchor_fails_closed()
    {
        await using var dataSource = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var clear =
            new NpgsqlCommand(
                "TRUNCATE banking.authorization_security_recovery_checkpoint, banking.authorization_context_tombstone, banking.authorization_context_writer, banking.authorization_context;",
                connection);
        await clear.ExecuteNonQueryAsync();

        var anchor = new InMemoryAuthorizationRecoverySequenceAnchor();
        var coordinator = new PostgresAuthorizationRecoveryCoordinator(dataSource, anchor);
        await using (var tx = await connection.BeginTransactionAsync())
        {
            await coordinator.SealAsync(connection, tx, 3);
            await tx.CommitAsync();
        }

        var result = await coordinator.VerifyAsync();
        Assert.False(result.IsConsistent);
        Assert.Contains("ahead", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PrepareAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}