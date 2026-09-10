using System.Security.Cryptography;
using System.Text;
using Foundgine.Runtime.ControlPlane;
using Npgsql;

namespace Foundgine.HighAssurance.Postgres.Execution;

/// <summary>
/// Crash-recovery boundary for durable authorization state. PostgreSQL is the source of
/// truth; an application process must never restore pre-crash in-memory authority without
/// first validating the durable security-state checkpoint.
/// </summary>
public sealed class PostgresAuthorizationRecoveryCoordinator
{
    private const long CheckpointId = 1;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IAuthorizationRecoverySequenceAnchor _anchor;

    public PostgresAuthorizationRecoveryCoordinator(
        NpgsqlDataSource dataSource,
        IAuthorizationRecoverySequenceAnchor? anchor = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _anchor = anchor ?? new InMemoryAuthorizationRecoverySequenceAnchor();
    }

    /// <summary>Returns the durable state digest without publishing any in-memory authority.</summary>
    public async Task<string> ComputeStateDigestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await ComputeStateDigestAsync(connection, null, cancellationToken);
    }

    /// <summary>
    /// Seals the exact database state inside the caller's transaction. The checkpoint is
    /// therefore committed atomically with the security transition.
    /// </summary>
    public async Task SealAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long sequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));

        var anchored = await _anchor.ReadAsync(cancellationToken);
        if (sequence <= anchored)
            throw new InvalidOperationException(
                $"Recovery checkpoint sequence {sequence} is not greater than durable anchor {anchored}; rollback/replay rejected.");

        var digest = await ComputeStateDigestAsync(connection, transaction, cancellationToken);
        const string sql = """
                           INSERT INTO banking.authorization_security_recovery_checkpoint(checkpoint_id, sequence, state_digest, status)
                           VALUES (1, @sequence, @digest, 'sealed')
                           ON CONFLICT (checkpoint_id) DO UPDATE
                           SET sequence = EXCLUDED.sequence,
                               state_digest = EXCLUDED.state_digest,
                               status = EXCLUDED.status,
                               updated_at = now();
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("digest", digest);
        await command.ExecuteNonQueryAsync(cancellationToken);

        // The external anchor is deliberately advanced only after the caller commits the
        // surrounding transaction. SealAsync therefore records intent in PostgreSQL; callers
        // must call AdvanceAnchorAfterCommitAsync only after a successful COMMIT.
    }

    public async Task AdvanceAnchorAfterCommitAsync(long sequence, CancellationToken cancellationToken = default)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (!await _anchor.AdvanceAsync(sequence, cancellationToken))
            throw new InvalidOperationException($"Recovery sequence {sequence} cannot advance the durable anchor.");
    }

    /// <summary>
    /// Validates durable state after process/database recovery. Any mismatch fails closed.
    /// </summary>
    public async Task<AuthorizationRecoveryResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql =
            "SELECT sequence, state_digest, status FROM banking.authorization_security_recovery_checkpoint WHERE checkpoint_id = 1;";
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return AuthorizationRecoveryResult.Fail(
                "No durable authorization recovery checkpoint exists; authority must not be restored.");

        var sequence = reader.GetInt64(0);
        var anchored = await _anchor.ReadAsync(cancellationToken);
        if (sequence < anchored)
            return AuthorizationRecoveryResult.Fail(
                "Recovery checkpoint sequence is older than the durable monotonic anchor; rollback/resurrection rejected.");
        if (sequence > anchored)
            return AuthorizationRecoveryResult.Fail(
                "Recovery checkpoint is ahead of the durable monotonic anchor; commit/anchor reconciliation is required.");

        var expected = reader.GetString(1);
        var status = reader.GetString(2);
        if (!string.Equals(status, "sealed", StringComparison.Ordinal))
            return AuthorizationRecoveryResult.Fail(
                "Authorization recovery checkpoint is not sealed; authority must not be restored.");

        await reader.CloseAsync();
        var actual = await ComputeStateDigestAsync(connection, null, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)))
            return AuthorizationRecoveryResult.Fail(
                "Durable authorization state differs from the last committed checkpoint; fail closed.");

        return AuthorizationRecoveryResult.Success(sequence, actual);
    }

    private static async Task<string> ComputeStateDigestAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        var canonical = new StringBuilder(4096);
        await AppendRowsAsync(connection, transaction, cancellationToken, canonical,
            "SELECT actor_id::text, tenant_id::text, allowed::text, version::text, fingerprint, integrity_algorithm, integrity_key_id, integrity_tag FROM banking.authorization_context ORDER BY tenant_id, actor_id");
        await AppendRowsAsync(connection, transaction, cancellationToken, canonical,
            "SELECT writer_id::text, actor_id::text, tenant_id::text, active::text, database_role, credential_fingerprint, last_write_sequence::text FROM banking.authorization_context_writer ORDER BY tenant_id, actor_id, writer_id");
        await AppendRowsAsync(connection, transaction, cancellationToken, canonical,
            "SELECT actor_id::text, tenant_id::text, last_version::text, last_fingerprint, integrity_algorithm, integrity_key_id, integrity_tag FROM banking.authorization_context_tombstone ORDER BY tenant_id, actor_id");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static async Task AppendRowsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        CancellationToken cancellationToken, StringBuilder canonical, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            canonical.Append('|');
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? "<NULL>" : reader.GetValue(i).ToString()!;
                canonical.Append(value.Length).Append(':').Append(value).Append(';');
            }

            canonical.Append('\n');
        }
    }
}

public sealed record AuthorizationRecoveryResult(
    bool IsConsistent,
    long Sequence,
    string? Digest,
    string? FailureReason)
{
    public static AuthorizationRecoveryResult Success(long sequence, string digest) =>
        new(true, sequence, digest, null);

    public static AuthorizationRecoveryResult Fail(string reason) => new(false, 0, null, reason);
}