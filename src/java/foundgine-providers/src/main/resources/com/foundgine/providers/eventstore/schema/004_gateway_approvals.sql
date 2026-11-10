-- Sensitive approval payloads are encoded/encrypted by the host-supplied codec.
CREATE TABLE IF NOT EXISTS foundgine_gateway_approvals
(
    handle_digest varchar(128) PRIMARY KEY,
    subject varchar(200) NOT NULL,
    tenant_id varchar(200) NULL,
    approval_payload jsonb NOT NULL,
    plan_fingerprint varchar(256) NOT NULL,
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL,
    CONSTRAINT ck_foundgine_gateway_approval_digest CHECK (handle_digest ~ '^[0-9A-Fa-f]{64}$')
);
CREATE INDEX IF NOT EXISTS ix_foundgine_gateway_approvals_expiry ON foundgine_gateway_approvals(expires_at);
