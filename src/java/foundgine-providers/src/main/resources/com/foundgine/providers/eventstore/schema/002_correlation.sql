ALTER TABLE foundgine_events ADD COLUMN IF NOT EXISTS correlation_id varchar(200) NULL;

CREATE INDEX IF NOT EXISTS ix_foundgine_events_correlation
    ON foundgine_events (tenant_id, stream_id, correlation_id, sequence)
    WHERE correlation_id IS NOT NULL;
