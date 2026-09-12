CREATE SCHEMA IF NOT EXISTS banking;

CREATE TABLE IF NOT EXISTS banking.bank_account
(
    id                  uuid PRIMARY KEY,
    tenant_id           integer NOT NULL,
    owner_id            uuid NOT NULL,
    balance             numeric(19,4) NOT NULL,
    pending_transactions numeric(19,4) NOT NULL DEFAULT 0,
    regulatory_hold     numeric(19,4) NOT NULL DEFAULT 0,
    daily_transferred   numeric(19,4) NOT NULL DEFAULT 0,
    daily_limit         numeric(19,4) NOT NULL,
    is_frozen           boolean NOT NULL DEFAULT false,
    CONSTRAINT ck_bank_account_amounts CHECK (balance >= 0 AND pending_transactions >= 0 AND regulatory_hold >= 0 AND daily_transferred >= 0 AND daily_limit >= 0)
);

CREATE TABLE IF NOT EXISTS banking.transfer_idempotency
(
    idempotency_key       text PRIMARY KEY,
    actor_id              uuid NOT NULL,
    tenant_id             integer NOT NULL,
    source_account_id     uuid NOT NULL,
    destination_account_id uuid NOT NULL,
    amount                numeric(19,4) NOT NULL,
    transfer_id           uuid NOT NULL UNIQUE,
    source_balance        numeric(19,4) NOT NULL,
    destination_balance   numeric(19,4) NOT NULL,
    created_at            timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS banking.transfer_audit
(
    id                    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    transfer_id           uuid NOT NULL,
    action                text NOT NULL,
    actor_id              uuid NOT NULL,
    tenant_id             integer NOT NULL,
    source_account_id     uuid NOT NULL,
    destination_account_id uuid NOT NULL,
    amount                numeric(19,4) NOT NULL,
    created_at            timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS banking.authorization_context
(
    actor_id     uuid NOT NULL,
    tenant_id    integer NOT NULL,
    allowed      boolean NOT NULL,
    version      bigint NOT NULL,
    fingerprint  text NOT NULL,
    integrity_algorithm text NOT NULL,
    integrity_key_id text NOT NULL,
    integrity_tag text NOT NULL,
    PRIMARY KEY (actor_id, tenant_id),
    CONSTRAINT ck_authorization_context_version CHECK (version > 0),
    CONSTRAINT ck_authorization_context_fingerprint CHECK (length(btrim(fingerprint)) > 0),
    CONSTRAINT ck_authorization_context_integrity_algorithm CHECK (integrity_algorithm = 'HMAC-SHA256/v1'),
    CONSTRAINT ck_authorization_context_integrity_key CHECK (length(btrim(integrity_key_id)) > 0),
    CONSTRAINT ck_authorization_context_integrity_tag CHECK (length(btrim(integrity_tag)) = 64)
);

CREATE TABLE IF NOT EXISTS banking.authorization_context_writer
(
    writer_id              uuid NOT NULL PRIMARY KEY,
    actor_id               uuid NOT NULL,
    tenant_id              integer NOT NULL,
    active                 boolean NOT NULL DEFAULT true,
    database_role          text NOT NULL,
    credential_fingerprint text NOT NULL,
    last_write_sequence    bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_authorization_writer_sequence CHECK (last_write_sequence >= 0),
    CONSTRAINT ck_authorization_writer_credential CHECK (length(btrim(credential_fingerprint)) > 0)
);

CREATE TABLE IF NOT EXISTS banking.authorization_context_tombstone
(
    actor_id         uuid NOT NULL,
    tenant_id        integer NOT NULL,
    last_version     bigint NOT NULL,
    last_fingerprint text NOT NULL,
    integrity_algorithm text NOT NULL,
    integrity_key_id text NOT NULL,
    integrity_tag text NOT NULL,
    PRIMARY KEY (actor_id, tenant_id),
    CONSTRAINT ck_authorization_context_tombstone_version CHECK (last_version > 0),
    CONSTRAINT ck_authorization_context_tombstone_fingerprint CHECK (length(btrim(last_fingerprint)) > 0),
    CONSTRAINT ck_authorization_context_tombstone_algorithm CHECK (integrity_algorithm = 'HMAC-SHA256/v1'),
    CONSTRAINT ck_authorization_context_tombstone_key CHECK (length(btrim(integrity_key_id)) > 0),
    CONSTRAINT ck_authorization_context_tombstone_tag CHECK (length(btrim(integrity_tag)) = 64)
);

-- M5.20 integrity metadata migration for databases created by earlier milestones.
ALTER TABLE banking.authorization_context
    ADD COLUMN IF NOT EXISTS integrity_algorithm text NOT NULL DEFAULT 'HMAC-SHA256/v1';
ALTER TABLE banking.authorization_context
    ADD COLUMN IF NOT EXISTS integrity_key_id text NOT NULL DEFAULT 'migration-required';
ALTER TABLE banking.authorization_context
    ADD COLUMN IF NOT EXISTS integrity_tag text NOT NULL DEFAULT repeat('0', 64);
ALTER TABLE banking.authorization_context_tombstone
    ADD COLUMN IF NOT EXISTS integrity_algorithm text NOT NULL DEFAULT 'HMAC-SHA256/v1';
ALTER TABLE banking.authorization_context_tombstone
    ADD COLUMN IF NOT EXISTS integrity_key_id text NOT NULL DEFAULT 'migration-required';
ALTER TABLE banking.authorization_context_tombstone
    ADD COLUMN IF NOT EXISTS integrity_tag text NOT NULL DEFAULT repeat('0', 64);


-- M5.35 durable recovery checkpoint. The checkpoint is sealed in the same transaction
-- as the security transition. A missing/mismatched checkpoint fails closed on recovery.
CREATE TABLE IF NOT EXISTS banking.authorization_security_recovery_checkpoint
(
    checkpoint_id bigint PRIMARY KEY CHECK (checkpoint_id = 1),
    sequence      bigint NOT NULL CHECK (sequence > 0),
    state_digest  text NOT NULL CHECK (length(state_digest) = 64),
    status        text NOT NULL CHECK (status IN ('sealed', 'recovery-required')),
    updated_at    timestamptz NOT NULL DEFAULT now()
);
