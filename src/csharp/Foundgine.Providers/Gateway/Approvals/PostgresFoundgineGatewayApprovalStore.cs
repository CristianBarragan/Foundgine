using Foundgine.Providers.Outbox;
using Foundgine.Runtime;
using Npgsql;
using NpgsqlTypes;

namespace Foundgine.Providers.Gateway.Approvals;

/// <summary>Application-supplied, AOT-safe codec. Implementations should encrypt approval payloads at rest.</summary>
public interface IFoundgineGatewayApprovalPayloadCodec
{
    string EncodeApproval(MutationPlanApproval approval);
    MutationPlanApproval DecodeApproval(string payload);
    string EncodeEvidence(FoundgineGatewayApprovalEvidence evidence);
}

/// <summary>PostgreSQL approval store. Approval and ApprovalRequested outbox row share one transaction.</summary>
public sealed class PostgresFoundgineGatewayApprovalStore : IAtomicFoundgineGatewayApprovalStore
{
    /// <summary>Outbox routing partition for approvals without a tenant. Real tenants may never use this id.</summary>
    public const string GlobalOutboxPartition = "__foundgine_global__";
    private static readonly System.Text.RegularExpressions.Regex DigestPattern = new("^[0-9A-Fa-f]{64}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex EventIdPattern = new("^[0-9A-Fa-f]{32}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly NpgsqlDataSource _dataSource;
    private readonly IFoundgineGatewayApprovalPayloadCodec _codec;
    private readonly bool _enforceDatabaseClock;

    /// <param name="codec"></param>
    /// <param name="enforceDatabaseClock">
    /// When true (default), <see cref="TakeAsync"/> also requires <c>expires_at &gt; clock_timestamp()</c> on the database
    /// server, so an approval is valid only while BOTH the caller's clock and the database clock say it is unexpired;
    /// application-node clock skew can never extend an approval beyond the database's view. Disable only in tests that
    /// drive time with a fixed clock.
    /// </param>
    /// <param name="dataSource"></param>
    public PostgresFoundgineGatewayApprovalStore(NpgsqlDataSource dataSource, IFoundgineGatewayApprovalPayloadCodec codec,
        bool enforceDatabaseClock = true)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _enforceDatabaseClock = enforceDatabaseClock;
    }

    public async ValueTask StoreWithEvidenceAsync(string handleDigest, MutationPlanApproval approval,
        FoundgineGatewayApprovalEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handleDigest); ArgumentNullException.ThrowIfNull(approval); ArgumentNullException.ThrowIfNull(evidence);
        if (!string.Equals(approval.PlanFingerprint, evidence.PlanFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Approval and evidence plan fingerprints differ.");
        if (!string.Equals(evidence.EventType, "ApprovalRequested", StringComparison.Ordinal) ||
            !string.Equals(approval.Request.Security?.Subject, evidence.Subject, StringComparison.Ordinal) ||
            !string.Equals(approval.Request.Security?.Tenant, evidence.Tenant, StringComparison.Ordinal) ||
            evidence.ExpiresAt <= evidence.OccurredAt)
            throw new InvalidOperationException("Approval evidence does not match the approved identity, event type or validity interval.");
        if (!DigestPattern.IsMatch(handleDigest))
            throw new ArgumentException("Approval handle digest must be 64 hexadecimal characters.", nameof(handleDigest));
        if (string.Equals(evidence.Tenant, GlobalOutboxPartition, StringComparison.Ordinal))
            throw new InvalidOperationException($"Tenant id '{GlobalOutboxPartition}' is reserved for the global outbox partition.");
        if (!EventIdPattern.IsMatch(evidence.EventId))
            throw new ArgumentException("Evidence event id must be 32 hexadecimal characters (UUID 'N' format).", nameof(evidence));
        var eventGuid = Guid.ParseExact(evidence.EventId, "N");
        // Npgsql only writes timestamptz values with offset 0.
        var expiresUtc = evidence.ExpiresAt.ToUniversalTime();
        var occurredUtc = evidence.OccurredAt.ToUniversalTime();
        var payload = _codec.EncodeApproval(approval);
        var eventPayload = _codec.EncodeEvidence(evidence);
        await using var c = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await c.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO foundgine_gateway_approvals(handle_digest, subject, tenant_id, approval_payload, plan_fingerprint, expires_at, created_at)
                VALUES (@digest, @subject, @tenant, @payload, @fingerprint, @expires, @created)
                """, c, tx))
            {
                insert.Parameters.AddWithValue("digest", handleDigest);
                insert.Parameters.AddWithValue("subject", evidence.Subject);
                insert.Parameters.Add("tenant", NpgsqlDbType.Varchar).Value = (object?)evidence.Tenant ?? DBNull.Value;
                insert.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = payload;
                insert.Parameters.AddWithValue("fingerprint", evidence.PlanFingerprint);
                insert.Parameters.AddWithValue("expires", expiresUtc);
                insert.Parameters.AddWithValue("created", occurredUtc);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            // The outbox tenant is a routing partition; the evidence payload retains the actual nullable tenant.
            await using (var outbox = new NpgsqlCommand("""
                INSERT INTO foundgine_outbox(tenant_id,id,topic,message_key,payload,headers,created_at,available_at)
                VALUES (@tenant,@id,@topic,@key,@payload::jsonb,'{}'::jsonb,@created,@created)
                """, c, tx))
            {
                outbox.Parameters.AddWithValue("tenant", evidence.Tenant ?? GlobalOutboxPartition);
                outbox.Parameters.AddWithValue("id", eventGuid);
                outbox.Parameters.AddWithValue("topic", evidence.EventType);
                // Key is the event id, never the handle digest (store-level lookup credential).
                outbox.Parameters.AddWithValue("key", eventGuid.ToString());
                outbox.Parameters.AddWithValue("payload", eventPayload);
                outbox.Parameters.AddWithValue("created", occurredUtc);
                await outbox.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); throw; }
    }

    public ValueTask StoreAsync(string handleDigest, MutationPlanApproval approval, string subject, string? tenant,
        DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new InvalidOperationException(
            "Use StoreWithEvidenceAsync; standalone approval persistence is prohibited."));

    /// <summary>
    /// Consumes an approval without holding a transaction or row lock across the codec. Phase 1 reads the payload (no
    /// lock); the codec decodes it outside any transaction, so a slow decryption call blocks nobody and pins no
    /// connection; phase 2 deletes the row with the same predicates and wins only if it is still present. A decode
    /// failure consumes nothing; concurrent consumers may both decode but exactly one delete succeeds.
    /// </summary>
    public async ValueTask<MutationPlanApproval?> TakeAsync(string handleDigest, string subject, string? tenant,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (handleDigest is null || !DigestPattern.IsMatch(handleDigest) || subject is null) return null;
        var match = "handle_digest=@digest AND subject=@subject AND tenant_id IS NOT DISTINCT FROM @tenant AND expires_at>@now"
            + (_enforceDatabaseClock ? " AND expires_at>clock_timestamp()" : "");
        string? payload;
        await using (var c = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        await using (var cmd = new NpgsqlCommand("SELECT approval_payload::text FROM foundgine_gateway_approvals WHERE " + match, c))
        {
            BindMatch(cmd, handleDigest, subject, tenant, now);
            payload = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }
        if (payload is null) return null;

        var approval = _codec.DecodeApproval(payload); // may throw: nothing consumed yet

        await using (var c = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        await using (var cmd = new NpgsqlCommand("DELETE FROM foundgine_gateway_approvals WHERE " + match, c))
        {
            BindMatch(cmd, handleDigest, subject, tenant, now);
            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1 ? approval : null;
        }
    }

    private static void BindMatch(NpgsqlCommand cmd, string handleDigest, string subject, string? tenant, DateTimeOffset now)
    {
        cmd.Parameters.AddWithValue("digest", handleDigest); cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.Add("tenant", NpgsqlDbType.Varchar).Value = (object?)tenant ?? DBNull.Value;
        cmd.Parameters.AddWithValue("now", now.ToUniversalTime());
    }

    public async ValueTask<bool> RevokeAsync(string handleDigest, string subject, string? tenant,
        CancellationToken cancellationToken = default)
    {
        if (handleDigest is null || !DigestPattern.IsMatch(handleDigest) || subject is null) return false;
        await using var c = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM foundgine_gateway_approvals
            WHERE handle_digest=@digest AND subject=@subject AND tenant_id IS NOT DISTINCT FROM @tenant
            """, c);
        cmd.Parameters.AddWithValue("digest", handleDigest); cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.Add("tenant", NpgsqlDbType.Varchar).Value = (object?)tenant ?? DBNull.Value;
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Deletes approvals whose expiry is at or before <paramref name="now"/>. <see cref="TakeAsync"/> never removes
    /// expired rows, so hosts must run this periodically. Removes at most <paramref name="batchSize"/> rows per call;
    /// repeat until the result is smaller than the batch size.
    /// </summary>
    public async ValueTask<int> PurgeExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        await using var c = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM foundgine_gateway_approvals WHERE handle_digest IN (
                SELECT handle_digest FROM foundgine_gateway_approvals
                WHERE expires_at<=@now ORDER BY expires_at LIMIT @limit FOR UPDATE SKIP LOCKED)
            """, c);
        cmd.Parameters.AddWithValue("now", now.ToUniversalTime());
        cmd.Parameters.AddWithValue("limit", batchSize);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
