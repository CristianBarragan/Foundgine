using Foundgine.Runtime.ControlPlane;
using Npgsql;

namespace Foundgine.HighAssurance.Postgres.Execution;

/// <summary>
/// PostgreSQL-backed authorization evidence whose row lock is held until the
/// surrounding mutation transaction commits or rolls back.
///
/// Authorization lifecycle writes additionally require an explicitly registered
/// writer provenance. The writer is bound to a tenant, actor, PostgreSQL role,
/// and monotonically increasing write sequence.
/// </summary>
public sealed class PostgresAuthorizationContextStore
{
    private readonly AuthorizationContextIntegrityKeyRingManager _integrityManager;
    private AuthorizationContextIntegrityKeyRing Integrity => _integrityManager.Snapshot;

    public PostgresAuthorizationContextStore(AuthorizationContextIntegrityKeyRing integrity)
    {
        ArgumentNullException.ThrowIfNull(integrity);
        _integrityManager = new AuthorizationContextIntegrityKeyRingManager(integrity);
    }

    public PostgresAuthorizationContextStore(AuthorizationContextIntegrityKeyRingManager integrityManager)
    {
        _integrityManager = integrityManager ?? throw new ArgumentNullException(nameof(integrityManager));
    }

    public AuthorizationContextIntegrityKeyRing CurrentIntegrityKeyRing => Integrity;

    /// <summary>
    /// Returns the integrity key ids still referenced by live authorization evidence
    /// or lifecycle tombstones. Call this inside the same administrative transaction
    /// that coordinates a retirement decision.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetPersistedIntegrityKeyIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT integrity_key_id FROM banking.authorization_context
                           UNION
                           SELECT integrity_key_id FROM banking.authorization_context_tombstone;
                           """;
        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetString(0));
        return result;
    }


    public async Task<AuthorizationContextRow?> LoadForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorId,
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT actor_id, tenant_id, allowed, version, fingerprint, integrity_algorithm, integrity_key_id, integrity_tag
                           FROM banking.authorization_context
                           WHERE actor_id = @actor AND tenant_id = @tenant
                           FOR UPDATE;
                           """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var row = new AuthorizationContextRow(
            reader.GetGuid(0), reader.GetInt32(1), reader.GetBoolean(2),
            reader.GetInt64(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7));
        if (!Integrity.VerifyContextTag(row.ActorId, row.TenantId, row.Allowed, row.Version, row.Fingerprint,
                row.IntegrityAlgorithm, row.IntegrityKeyId, row.IntegrityTag))
            throw new InvalidOperationException(
                "Authorization context integrity verification failed; authorization fails closed.");
        return row;
    }

    public Task CreateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, int tenantId, bool allowed, long version, string fingerprint,
        CancellationToken cancellationToken = default) =>
        CreateAsync(connection, transaction, actorId, tenantId, allowed, version, fingerprint,
            new AuthorizationWriteProvenance(Guid.Empty, actorId, tenantId, 0, ""), cancellationToken);

    public async Task CreateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, int tenantId, bool allowed, long version, string fingerprint,
        AuthorizationWriteProvenance provenance, CancellationToken cancellationToken = default)
    {
        ValidateVersion(version);
        ValidateFingerprint(fingerprint);
        await ValidateWriterAsync(connection, transaction, provenance, actorId, tenantId, cancellationToken);

        const string tombstoneSql = """
                                    SELECT last_version, last_fingerprint, integrity_algorithm, integrity_key_id, integrity_tag FROM banking.authorization_context_tombstone
                                    WHERE actor_id = @actor AND tenant_id = @tenant FOR UPDATE;
                                    """;
        await using (var tombstoneCommand = new NpgsqlCommand(tombstoneSql, connection, transaction))
        {
            tombstoneCommand.Parameters.AddWithValue("actor", actorId);
            tombstoneCommand.Parameters.AddWithValue("tenant", tenantId);
            await using var tombstoneReader = await tombstoneCommand.ExecuteReaderAsync(cancellationToken);
            if (await tombstoneReader.ReadAsync(cancellationToken))
            {
                var lastVersion = tombstoneReader.GetInt64(0);
                var lastFingerprint = tombstoneReader.GetString(1);
                var algorithm = tombstoneReader.GetString(2);
                var keyId = tombstoneReader.GetString(3);
                var tag = tombstoneReader.GetString(4);
                if (!Integrity.VerifyTombstoneTag(actorId, tenantId, lastVersion, lastFingerprint, algorithm, keyId,
                        tag))
                    throw new InvalidOperationException(
                        "Authorization lifecycle tombstone integrity verification failed; authorization fails closed.");
                if (version <= lastVersion)
                    throw new InvalidOperationException(
                        $"Authorization context version must exceed the last committed lifecycle version. Last={lastVersion}, requested={version}.");
            }
        }

        const string sql = """
                           INSERT INTO banking.authorization_context(actor_id, tenant_id, allowed, version, fingerprint, integrity_algorithm, integrity_key_id, integrity_tag)
                           VALUES (@actor, @tenant, @allowed, @version, @fingerprint, @algorithm, @key_id, @tag);
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(command, actorId, tenantId, allowed, version, fingerprint);
        command.Parameters.AddWithValue("algorithm", AuthorizationContextIntegrityKeyRing.CurrentAlgorithmVersion);
        command.Parameters.AddWithValue("key_id", Integrity.ActiveKeyId);
        command.Parameters.AddWithValue("tag",
            Integrity.ComputeContextTag(actorId, tenantId, allowed, version, fingerprint));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task UpdateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, int tenantId, bool allowed, long version, string fingerprint,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(connection, transaction, actorId, tenantId, allowed, version, fingerprint,
            new AuthorizationWriteProvenance(Guid.Empty, actorId, tenantId, 0, ""), cancellationToken);

    public async Task UpdateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, int tenantId, bool allowed, long version, string fingerprint,
        AuthorizationWriteProvenance provenance, CancellationToken cancellationToken = default)
    {
        ValidateVersion(version);
        ValidateFingerprint(fingerprint);
        await ValidateWriterAsync(connection, transaction, provenance, actorId, tenantId, cancellationToken);

        var current = await LoadForUpdateAsync(connection, transaction, actorId, tenantId, cancellationToken)
                      ?? throw new InvalidOperationException("Authorization context does not exist.");
        if (version <= current.Version)
            throw new InvalidOperationException(
                $"Authorization context version must increase monotonically. Current={current.Version}, requested={version}.");

        const string sql = """
                           UPDATE banking.authorization_context
                           SET allowed = @allowed, version = @version, fingerprint = @fingerprint,
                               integrity_algorithm = @algorithm, integrity_key_id = @key_id, integrity_tag = @tag
                           WHERE actor_id = @actor AND tenant_id = @tenant;
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(command, actorId, tenantId, allowed, version, fingerprint);
        command.Parameters.AddWithValue("algorithm", AuthorizationContextIntegrityKeyRing.CurrentAlgorithmVersion);
        command.Parameters.AddWithValue("key_id", Integrity.ActiveKeyId);
        command.Parameters.AddWithValue("tag",
            Integrity.ComputeContextTag(actorId, tenantId, allowed, version, fingerprint));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Authorization context update did not affect exactly one row.");
    }

    public Task RevokeAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, int tenantId,
        long version, string fingerprint, CancellationToken cancellationToken = default) =>
        UpdateAsync(connection, transaction, actorId, tenantId, false, version, fingerprint, cancellationToken);

    public Task DeleteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, int tenantId,
        CancellationToken cancellationToken = default) =>
        DeleteAsync(connection, transaction, actorId, tenantId,
            new AuthorizationWriteProvenance(Guid.Empty, actorId, tenantId, 0, ""), cancellationToken);

    public async Task DeleteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, int tenantId,
        AuthorizationWriteProvenance provenance, CancellationToken cancellationToken = default)
    {
        await ValidateWriterAsync(connection, transaction, provenance, actorId, tenantId, cancellationToken);
        var current = await LoadForUpdateAsync(connection, transaction, actorId, tenantId, cancellationToken);
        if (current is null) return;

        const string tombstoneSql = """
                                    INSERT INTO banking.authorization_context_tombstone(actor_id, tenant_id, last_version, last_fingerprint, integrity_algorithm, integrity_key_id, integrity_tag)
                                    VALUES (@actor, @tenant, @version, @fingerprint, @algorithm, @key_id, @tag)
                                    ON CONFLICT (actor_id, tenant_id) DO UPDATE SET
                                        last_version = EXCLUDED.last_version, last_fingerprint = EXCLUDED.last_fingerprint,
                                        integrity_algorithm = EXCLUDED.integrity_algorithm, integrity_key_id = EXCLUDED.integrity_key_id, integrity_tag = EXCLUDED.integrity_tag;
                                    """;
        await using (var tombstone = new NpgsqlCommand(tombstoneSql, connection, transaction))
        {
            tombstone.Parameters.AddWithValue("actor", actorId);
            tombstone.Parameters.AddWithValue("tenant", tenantId);
            tombstone.Parameters.AddWithValue("version", current.Version);
            tombstone.Parameters.AddWithValue("fingerprint", current.Fingerprint);
            tombstone.Parameters.AddWithValue("algorithm",
                AuthorizationContextIntegrityKeyRing.CurrentAlgorithmVersion);
            tombstone.Parameters.AddWithValue("key_id", Integrity.ActiveKeyId);
            tombstone.Parameters.AddWithValue("tag",
                Integrity.ComputeTombstoneTag(actorId, tenantId, current.Version, current.Fingerprint));
            await tombstone.ExecuteNonQueryAsync(cancellationToken);
        }

        const string sql = "DELETE FROM banking.authorization_context WHERE actor_id = @actor AND tenant_id = @tenant;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("tenant", tenantId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Authorization context deletion did not affect exactly one row.");
    }

    private static async Task ValidateWriterAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        AuthorizationWriteProvenance provenance, Guid actorId, int tenantId,
        CancellationToken cancellationToken)
    {
        if (provenance.WriterId == Guid.Empty)
            throw new UnauthorizedAccessException("Authorization context writes require registered writer provenance.");
        if (provenance.ActorId != actorId || provenance.TenantId != tenantId)
            throw new UnauthorizedAccessException(
                "Authorization writer is not scoped to the target authorization identity.");
        if (provenance.WriteSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(provenance), "Writer sequence must be positive.");
        if (string.IsNullOrWhiteSpace(provenance.CredentialFingerprint))
            throw new ArgumentException("Writer credential fingerprint is required.", nameof(provenance));

        const string sql = """
                           SELECT tenant_id, actor_id, active, database_role, credential_fingerprint, last_write_sequence
                           FROM banking.authorization_context_writer
                           WHERE writer_id = @writer
                           FOR UPDATE;
                           """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("writer", provenance.WriterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new UnauthorizedAccessException("Authorization writer is not registered.");

        var writerTenant = reader.GetInt32(0);
        var writerActor = reader.GetGuid(1);
        var active = reader.GetBoolean(2);
        var databaseRole = reader.GetString(3);
        var credentialFingerprint = reader.GetString(4);
        var lastSequence = reader.GetInt64(5);

        if (!active || writerTenant != tenantId || writerActor != actorId)
            throw new UnauthorizedAccessException(
                "Authorization writer is inactive or outside its tenant/actor scope.");
        if (!string.Equals(credentialFingerprint, provenance.CredentialFingerprint, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                "Authorization writer credential provenance does not match the registered credential.");
        if (!string.Equals(databaseRole, await CurrentDatabaseRoleAsync(connection, transaction, cancellationToken),
                StringComparison.Ordinal))
            throw new UnauthorizedAccessException(
                "Authorization writer database-role provenance does not match the current PostgreSQL session.");
        if (provenance.WriteSequence <= lastSequence)
            throw new InvalidOperationException(
                $"Authorization writer sequence is stale. Last={lastSequence}, requested={provenance.WriteSequence}.");
        await reader.DisposeAsync();

        const string update =
            "UPDATE banking.authorization_context_writer SET last_write_sequence = @sequence WHERE writer_id = @writer;";
        await using var updateCommand = new NpgsqlCommand(update, connection, transaction);
        updateCommand.Parameters.AddWithValue("writer", provenance.WriterId);
        updateCommand.Parameters.AddWithValue("sequence", provenance.WriteSequence);
        if (await updateCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Authorization writer sequence update did not affect exactly one row.");
    }

    private static async Task<string> CurrentDatabaseRoleAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT current_user;", connection, transaction);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static void AddParameters(NpgsqlCommand command, Guid actorId, int tenantId, bool allowed, long version,
        string fingerprint)
    {
        command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("allowed", allowed);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
    }

    private static void ValidateVersion(long version)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), "Authorization context versions must be positive.");
    }

    private static void ValidateFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            throw new ArgumentException("Authorization context fingerprint cannot be empty.", nameof(fingerprint));
    }
}

public sealed record AuthorizationContextRow(
    Guid ActorId,
    int TenantId,
    bool Allowed,
    long Version,
    string Fingerprint,
    string IntegrityAlgorithm,
    string IntegrityKeyId,
    string IntegrityTag);

public sealed record AuthorizationWriteProvenance(
    Guid WriterId,
    Guid ActorId,
    int TenantId,
    long WriteSequence,
    string CredentialFingerprint);