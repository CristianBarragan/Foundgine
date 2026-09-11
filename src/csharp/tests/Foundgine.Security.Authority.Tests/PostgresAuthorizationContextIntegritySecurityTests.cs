using Foundgine.Runtime.ControlPlane;
using Foundgine.HighAssurance.Postgres.Execution;
using Npgsql;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>
/// : persisted authorization evidence is cryptographically bound to its
/// complete canonical security payload and an externally-held key.
/// </summary>
public sealed class PostgresAuthorizationContextIntegritySecurityTests
{
    [PostgresFact]
    public async Task Tampering_with_allowed_version_fingerprint_or_identity_is_detected()
    {
        await using var ds = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(ds);
        var actor = Guid.NewGuid();
        const int tenant = 701;
        await SeedAsync(ds, actor, tenant, true, 7, "auth-v7");
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());

        foreach (var mutation in new[]
                 {
                     "allowed = false",
                     "version = version + 1",
                     "fingerprint = 'tampered'"
                 })
        {
            await RestoreAsync(ds, actor, tenant, true, 7, "auth-v7");
            await TamperAsync(ds, actor, tenant, mutation);

            await using var c = await ds.OpenConnectionAsync();
            await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.LoadForUpdateAsync(c, tx, actor, tenant));
            await tx.RollbackAsync();
        }

        await RestoreAsync(ds, actor, tenant, true, 7, "auth-v7");
        var otherActor = Guid.NewGuid();
        await TamperIdentityAsync(ds, actor, tenant, otherActor);
        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.LoadForUpdateAsync(c, tx, otherActor, tenant));
            await tx.RollbackAsync();
        }
    }

    [PostgresFact]
    public async Task Unknown_key_and_algorithm_are_fail_closed()
    {
        await using var ds = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(ds);
        var actor = Guid.NewGuid();
        const int tenant = 702;
        await SeedAsync(ds, actor, tenant, true, 1, "auth-v1");
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());

        await TamperAsync(ds, actor, tenant, "integrity_key_id = 'unknown-key'");
        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadForUpdateAsync(c, tx, actor, tenant));
            await tx.RollbackAsync();
        }

        await RestoreAsync(ds, actor, tenant, true, 1, "auth-v1");
        await TamperAsync(ds, actor, tenant, "integrity_algorithm = 'HMAC-SHA512/v1'");
        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadForUpdateAsync(c, tx, actor, tenant));
            await tx.RollbackAsync();
        }
    }

    [PostgresFact]
    public async Task Valid_old_key_can_be_read_and_update_rotates_to_active_key()
    {
        await using var ds = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(ds);
        var oldKey =
            new AuthorizationContextIntegrityKey("key-v1", SHA256.HashData(Encoding.UTF8.GetBytes("old-key-material")));
        var newKey =
            new AuthorizationContextIntegrityKey("key-v2", SHA256.HashData(Encoding.UTF8.GetBytes("new-key-material")));
        var oldRing = new AuthorizationContextIntegrityKeyRing(oldKey);
        var rotatingRing = new AuthorizationContextIntegrityKeyRing(newKey, new[] { oldKey });
        var actor = Guid.NewGuid();
        const int tenant = 703;

        await SeedWithRingAsync(ds, actor, tenant, true, 4, "auth-v4", oldRing);
        var store = new PostgresAuthorizationContextStore(rotatingRing);

        await using var c = await ds.OpenConnectionAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var before = await store.LoadForUpdateAsync(c, tx, actor, tenant);
        Assert.NotNull(before);
        Assert.Equal("key-v1", before!.IntegrityKeyId);

        await store.UpdateAsync(c, tx, actor, tenant, true, 5, "auth-v5",
            await RegisterWriterForUpdateAsync(c, tx, actor, tenant, 703));
        await tx.CommitAsync();

        var metadata = await ReadIntegrityMetadataAsync(ds, actor, tenant);
        Assert.Equal("HMAC-SHA256/v1", metadata.Algorithm);
        Assert.Equal("key-v2", metadata.KeyId);
        Assert.Equal(64, metadata.Tag.Length);
    }

    [PostgresFact]
    public async Task Tampered_lifecycle_tombstone_is_detected_before_recreation()
    {
        await using var ds = NpgsqlDataSource.Create(PostgresTestEnvironment.ConnectionString);
        await PrepareAsync(ds);
        var actor = Guid.NewGuid();
        const int tenant = 704;
        var writer = await RegisterWriterAsync(ds, actor, tenant);
        var store = new PostgresAuthorizationContextStore(TestAuthorizationIntegrity.CreateKeyRing());

        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await store.CreateAsync(c, tx, actor, tenant, true, 8, "auth-v8", writer with { WriteSequence = 1 });
            await tx.CommitAsync();
        }

        var deleteWriter = writer with { WriteSequence = 2 };
        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await store.DeleteAsync(c, tx, actor, tenant, deleteWriter);
            await tx.CommitAsync();
        }

        await using (var c = await ds.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand(
                         "UPDATE banking.authorization_context_tombstone SET last_version = last_version + 1 WHERE actor_id=@actor AND tenant_id=@tenant;",
                         c))
        {
            cmd.Parameters.AddWithValue("actor", actor);
            cmd.Parameters.AddWithValue("tenant", tenant);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var c = await ds.OpenConnectionAsync())
        await using (var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.CreateAsync(c, tx, actor, tenant, true, 10, "auth-v10", writer with { WriteSequence = 3 }));
            await tx.RollbackAsync();
        }
    }

    [PostgresFact]
    public void Canonical_payload_prevents_delimiter_ambiguity()
    {
        var key = new AuthorizationContextIntegrityKey("test",
            SHA256.HashData(Encoding.UTF8.GetBytes("canonical-key")));
        var ring = new AuthorizationContextIntegrityKeyRing(key);
        var actor = Guid.NewGuid();
        var tagA = ring.ComputeContextTag(actor, 705, true, 1, "a|b:7");
        var tagB = ring.ComputeContextTag(actor, 705, true, 1, "a|b:8");
        Assert.NotEqual(tagA, tagB);
    }

    private static async Task SeedAsync(NpgsqlDataSource ds, Guid actor, int tenant, bool allowed, long version,
        string fingerprint) =>
        await SeedWithRingAsync(ds, actor, tenant, allowed, version, fingerprint,
            TestAuthorizationIntegrity.CreateKeyRing());

    private static async Task SeedWithRingAsync(NpgsqlDataSource ds, Guid actor, int tenant, bool allowed, long version,
        string fingerprint, AuthorizationContextIntegrityKeyRing ring)
    {
        await using var c = await ds.OpenConnectionAsync();
        const string sql = """
                           INSERT INTO banking.authorization_context(actor_id, tenant_id, allowed, version, fingerprint, integrity_algorithm, integrity_key_id, integrity_tag)
                           VALUES (@actor,@tenant,@allowed,@version,@fingerprint,@algorithm,@key_id,@tag)
                           ON CONFLICT (actor_id, tenant_id) DO UPDATE SET allowed=EXCLUDED.allowed, version=EXCLUDED.version,
                           fingerprint=EXCLUDED.fingerprint, integrity_algorithm=EXCLUDED.integrity_algorithm,
                           integrity_key_id=EXCLUDED.integrity_key_id, integrity_tag=EXCLUDED.integrity_tag;
                           """;
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("allowed", allowed);
        cmd.Parameters.AddWithValue("version", version);
        cmd.Parameters.AddWithValue("fingerprint", fingerprint);
        cmd.Parameters.AddWithValue("algorithm", AuthorizationContextIntegrityKeyRing.CurrentAlgorithmVersion);
        cmd.Parameters.AddWithValue("key_id", ring.ActiveKeyId);
        cmd.Parameters.AddWithValue("tag", ring.ComputeContextTag(actor, tenant, allowed, version, fingerprint));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RestoreAsync(NpgsqlDataSource ds, Guid actor, int tenant, bool allowed, long version,
        string fingerprint) =>
        await SeedAsync(ds, actor, tenant, allowed, version, fingerprint);

    private static async Task TamperAsync(NpgsqlDataSource ds, Guid actor, int tenant, string assignment)
    {
        await using var c = await ds.OpenConnectionAsync();
        await using var cmd =
            new NpgsqlCommand(
                $"UPDATE banking.authorization_context SET {assignment} WHERE actor_id=@actor AND tenant_id=@tenant;",
                c);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("tenant", tenant);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task TamperIdentityAsync(NpgsqlDataSource ds, Guid actor, int tenant, Guid newActor)
    {
        await using var c = await ds.OpenConnectionAsync();
        await using var cmd =
            new NpgsqlCommand(
                "UPDATE banking.authorization_context SET actor_id=@new_actor WHERE actor_id=@actor AND tenant_id=@tenant;",
                c);
        cmd.Parameters.AddWithValue("new_actor", newActor);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("tenant", tenant);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(string Algorithm, string KeyId, string Tag)> ReadIntegrityMetadataAsync(
        NpgsqlDataSource ds, Guid actor, int tenant)
    {
        await using var c = await ds.OpenConnectionAsync();
        await using var cmd =
            new NpgsqlCommand(
                "SELECT integrity_algorithm, integrity_key_id, integrity_tag FROM banking.authorization_context WHERE actor_id=@actor AND tenant_id=@tenant;",
                c);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("tenant", tenant);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        return (r.GetString(0), r.GetString(1), r.GetString(2));
    }

    private static async Task<AuthorizationWriteProvenance> RegisterWriterAsync(NpgsqlDataSource ds, Guid actor,
        int tenant)
    {
        var writer = Guid.NewGuid();
        const string credential = "integrity-writer";
        await using var c = await ds.OpenConnectionAsync();
        await using var cmd =
            new NpgsqlCommand(
                "INSERT INTO banking.authorization_context_writer(writer_id, actor_id, tenant_id, active, database_role, credential_fingerprint) VALUES (@writer,@actor,@tenant,true,current_user,@credential);",
                c);
        cmd.Parameters.AddWithValue("writer", writer);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("credential", credential);
        await cmd.ExecuteNonQueryAsync();
        return new AuthorizationWriteProvenance(writer, actor, tenant, 1, credential);
    }

    private static async Task<AuthorizationWriteProvenance> RegisterWriterForUpdateAsync(NpgsqlConnection c,
        NpgsqlTransaction tx, Guid actor, int tenant, int writerTenant)
    {
        var writer = Guid.NewGuid();
        const string credential = "rotation-writer";
        await using var cmd =
            new NpgsqlCommand(
                "INSERT INTO banking.authorization_context_writer(writer_id, actor_id, tenant_id, active, database_role, credential_fingerprint) VALUES (@writer,@actor,@tenant,true,current_user,@credential);",
                c, tx);
        cmd.Parameters.AddWithValue("writer", writer);
        cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("tenant", writerTenant);
        cmd.Parameters.AddWithValue("credential", credential);
        await cmd.ExecuteNonQueryAsync();
        return new AuthorizationWriteProvenance(writer, actor, tenant, 1, credential);
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