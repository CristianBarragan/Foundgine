CREATE TABLE IF NOT EXISTS foundgine_event_streams
(
    tenant_id varchar(200) NOT NULL,
    stream_id varchar(200) NOT NULL,
    last_sequence bigint NOT NULL CHECK (last_sequence >= 0),
    PRIMARY KEY (tenant_id, stream_id)
);

CREATE TABLE IF NOT EXISTS foundgine_events
(
    tenant_id varchar(200) NOT NULL,
    stream_id varchar(200) NOT NULL,
    sequence bigint NOT NULL CHECK (sequence > 0),
    event_id uuid NOT NULL,
    event_type varchar(200) NOT NULL,
    occurred_at timestamptz NOT NULL,
    payload jsonb NOT NULL,
    PRIMARY KEY (tenant_id, stream_id, sequence),
    UNIQUE (tenant_id, event_id)
);

CREATE INDEX IF NOT EXISTS ix_foundgine_events_retention
    ON foundgine_events (occurred_at);
