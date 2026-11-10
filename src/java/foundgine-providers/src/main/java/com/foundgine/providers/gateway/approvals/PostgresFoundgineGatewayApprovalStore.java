package com.foundgine.providers.gateway.approvals;

import com.foundgine.runtime.MutationPlanApproval;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.Objects;
import java.util.Optional;
import java.util.UUID;
import java.util.regex.Pattern;
import javax.sql.DataSource;

/** PostgreSQL store: approval state and its ApprovalRequested outbox row commit in one transaction. */
public final class PostgresFoundgineGatewayApprovalStore implements IAtomicFoundgineGatewayApprovalStore {
    /**
     * Outbox routing partition used for approvals that have no tenant. The outbox {@code tenant_id} column is
     * NOT NULL, so global-scope events use this reserved value. Real tenants may never use it.
     */
    public static final String GLOBAL_OUTBOX_PARTITION = "__foundgine_global__";

    private static final Pattern DIGEST = Pattern.compile("[0-9A-Fa-f]{64}");
    private static final Pattern EVENT_ID = Pattern.compile("[0-9A-Fa-f]{32}");

    private final DataSource dataSource;
    private final FoundgineGatewayApprovalPayloadCodec codec;
    private final boolean enforceDatabaseClock;

    public PostgresFoundgineGatewayApprovalStore(DataSource dataSource, FoundgineGatewayApprovalPayloadCodec codec) {
        this(dataSource, codec, true);
    }

    /** Database-clock enforcement is conservative: an approval must be live by both caller and database clocks. */
    public PostgresFoundgineGatewayApprovalStore(DataSource dataSource, FoundgineGatewayApprovalPayloadCodec codec,
            boolean enforceDatabaseClock) {
        if (dataSource == null || codec == null) throw new IllegalArgumentException("dataSource and codec are required");
        this.dataSource = dataSource; this.codec = codec; this.enforceDatabaseClock = enforceDatabaseClock;
    }

    @Override public void storeWithEvidence(String digest, MutationPlanApproval approval, FoundgineGatewayApprovalEvidence evidence) {
        if (digest == null || approval == null || evidence == null)
            throw new IllegalArgumentException("digest, approval and evidence are required");
        if (!DIGEST.matcher(digest).matches())
            throw new IllegalArgumentException("Approval handle digest must be 64 hexadecimal characters.");
        if (!approval.planFingerprint().equals(evidence.planFingerprint()))
            throw new IllegalArgumentException("Approval and evidence plan fingerprints differ.");
        var security = approval.request().security();
        if (!"ApprovalRequested".equals(evidence.eventType()) || security == null
                || !Objects.equals(security.subject(), evidence.subject())
                || !Objects.equals(security.tenant(), evidence.tenant())
                || !evidence.expiresAt().isAfter(evidence.occurredAt()))
            throw new IllegalArgumentException("Approval evidence does not match the approved identity, event type or validity interval.");
        if (GLOBAL_OUTBOX_PARTITION.equals(evidence.tenant()))
            throw new IllegalArgumentException("Tenant id '" + GLOBAL_OUTBOX_PARTITION + "' is reserved for the global outbox partition.");
        if (!EVENT_ID.matcher(evidence.eventId()).matches())
            throw new IllegalArgumentException("Evidence event id must be 32 hexadecimal characters (UUID 'N' format).");
        UUID eventUuid = UUID.fromString(formatUuid(evidence.eventId()));
        String approvalJson = codec.encodeApproval(approval);
        String evidenceJson = codec.encodeEvidence(evidence);
        try (Connection c = dataSource.getConnection()) {
            boolean oldAutoCommit = c.getAutoCommit();
            c.setAutoCommit(false);
            try {
                try (PreparedStatement p = c.prepareStatement("""
                        INSERT INTO foundgine_gateway_approvals
                            (handle_digest,subject,tenant_id,approval_payload,plan_fingerprint,expires_at,created_at)
                        VALUES (?,?,?,?::jsonb,?,?,?)""")) {
                    p.setString(1, digest); p.setString(2, evidence.subject()); p.setString(3, evidence.tenant());
                    p.setString(4, approvalJson); p.setString(5, evidence.planFingerprint());
                    p.setObject(6, OffsetDateTime.ofInstant(evidence.expiresAt(), ZoneOffset.UTC));
                    p.setObject(7, OffsetDateTime.ofInstant(evidence.occurredAt(), ZoneOffset.UTC));
                    p.executeUpdate();
                }
                // The outbox tenant is a routing partition; the event payload retains the actual nullable tenant.
                // The message key is the event id, never the handle digest: the digest is the store-level lookup
                // credential and must not leak to brokers, logs or dead-letter tooling.
                try (PreparedStatement p = c.prepareStatement("""
                        INSERT INTO foundgine_outbox(tenant_id,id,topic,message_key,payload,headers,created_at,available_at)
                        VALUES (?,?,?, ?,?::jsonb,'{}'::jsonb,?,?)""")) {
                    var at = OffsetDateTime.ofInstant(evidence.occurredAt(), ZoneOffset.UTC);
                    p.setString(1, evidence.tenant() == null ? GLOBAL_OUTBOX_PARTITION : evidence.tenant());
                    p.setObject(2, eventUuid); p.setString(3, evidence.eventType());
                    p.setString(4, eventUuid.toString()); p.setString(5, evidenceJson); p.setObject(6, at); p.setObject(7, at);
                    p.executeUpdate();
                }
                c.commit();
            } catch (Exception failure) {
                try { c.rollback(); } catch (SQLException rollback) { failure.addSuppressed(rollback); }
                if (failure instanceof RuntimeException runtime) throw runtime;
                throw new IllegalStateException("Approval transaction rolled back.", failure);
            } finally { try { c.setAutoCommit(oldAutoCommit); } catch (SQLException ignored) { } }
        } catch (SQLException e) { throw new IllegalStateException("Failed to atomically persist gateway approval and evidence.", e); }
        catch (RuntimeException e) { throw e; }
        catch (Exception e) { throw new IllegalStateException("Failed to atomically persist gateway approval and evidence.", e); }
    }

    @Override public void store(String digest, MutationPlanApproval approval, String subject, String tenant, Instant expiresAt) {
        throw new UnsupportedOperationException("Use storeWithEvidence; standalone approval persistence is prohibited.");
    }

    /**
     * Reads and decodes without a transaction/row lock, then conditionally deletes. Concurrent callers may decode,
     * but only one conditional DELETE can consume the row. Decode failures leave the approval available for retry.
     */
    @Override public Optional<MutationPlanApproval> take(String digest, String subject, String tenant, Instant now) {
        if (digest == null || !DIGEST.matcher(digest).matches() || subject == null || now == null) return Optional.empty();
        String payload;
        String clockPredicate = enforceDatabaseClock ? " AND expires_at>clock_timestamp()" : "";
        try (Connection c = dataSource.getConnection()) {
            try (PreparedStatement p = c.prepareStatement("""
                    SELECT approval_payload::text FROM foundgine_gateway_approvals
                    WHERE handle_digest=? AND subject=? AND tenant_id IS NOT DISTINCT FROM ? AND expires_at>?""" + clockPredicate)) {
                p.setString(1, digest); p.setString(2, subject); p.setString(3, tenant);
                p.setObject(4, OffsetDateTime.ofInstant(now, ZoneOffset.UTC));
                try (ResultSet rs = p.executeQuery()) { if (!rs.next()) return Optional.empty(); payload = rs.getString(1); }
            }
        } catch (SQLException e) { throw new IllegalStateException("Failed to read gateway approval.", e); }

        // Deliberately outside any transaction: codecs may perform slow decryption or host-specific work.
        MutationPlanApproval decoded = codec.decodeApproval(payload);
        try (Connection c = dataSource.getConnection(); PreparedStatement p = c.prepareStatement("""
                DELETE FROM foundgine_gateway_approvals
                WHERE handle_digest=? AND subject=? AND tenant_id IS NOT DISTINCT FROM ? AND expires_at>? AND approval_payload=?::jsonb""" + clockPredicate)) {
            p.setString(1, digest); p.setString(2, subject); p.setString(3, tenant);
            p.setObject(4, OffsetDateTime.ofInstant(now, ZoneOffset.UTC)); p.setString(5, payload);
            return p.executeUpdate() == 1 ? Optional.of(decoded) : Optional.empty();
        } catch (SQLException e) { throw new IllegalStateException("Failed to consume gateway approval.", e); }
    }

    @Override public boolean revoke(String digest, String subject, String tenant) {
        if (digest == null || !DIGEST.matcher(digest).matches() || subject == null) return false;
        try (Connection c = dataSource.getConnection(); PreparedStatement p = c.prepareStatement("""
                DELETE FROM foundgine_gateway_approvals
                WHERE handle_digest=? AND subject=? AND tenant_id IS NOT DISTINCT FROM ?""")) {
            p.setString(1, digest); p.setString(2, subject); p.setString(3, tenant); return p.executeUpdate() == 1;
        } catch (SQLException e) { throw new IllegalStateException("Failed to revoke gateway approval.", e); }
    }

    /**
     * Deletes approvals whose expiry is at or before {@code now}. Expired approvals can never be consumed, but
     * {@link #take} does not remove them, so hosts must run this periodically to bound table growth. Deletes at
     * most {@code batchSize} rows per call and returns how many were removed; call repeatedly until it returns
     * less than {@code batchSize}.
     */
    public int purgeExpired(Instant now, int batchSize) {
        if (now == null) throw new IllegalArgumentException("now is required");
        if (batchSize < 1) throw new IllegalArgumentException("batchSize must be positive");
        try (Connection c = dataSource.getConnection(); PreparedStatement p = c.prepareStatement("""
                DELETE FROM foundgine_gateway_approvals WHERE handle_digest IN (
                    SELECT handle_digest FROM foundgine_gateway_approvals
                    WHERE expires_at<=? ORDER BY expires_at LIMIT ? FOR UPDATE SKIP LOCKED)""")) {
            p.setObject(1, OffsetDateTime.ofInstant(now, ZoneOffset.UTC)); p.setInt(2, batchSize);
            return p.executeUpdate();
        } catch (SQLException e) { throw new IllegalStateException("Failed to purge expired gateway approvals.", e); }
    }

    private static String formatUuid(String value) {
        return value.substring(0,8)+"-"+value.substring(8,12)+"-"+value.substring(12,16)+"-"+value.substring(16,20)+"-"+value.substring(20);
    }
}
