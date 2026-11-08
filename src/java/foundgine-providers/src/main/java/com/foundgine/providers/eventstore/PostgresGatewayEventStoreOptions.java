package com.foundgine.providers.eventstore;

public final class PostgresGatewayEventStoreOptions {
    private String tenantId = "foundgine-gateway";
    private String streamId = "gateway-events";
    private int pageSize = 500;

    /** System tenant owning the gateway journal. Gateway events are not tenant-partitioned. */
    public String tenantId() { return tenantId; }
    public String streamId() { return streamId; }
    /** Rows per database round trip; must not exceed the event store's maximum replay batch. */
    public int pageSize() { return pageSize; }
    public PostgresGatewayEventStoreOptions tenantId(String value) { tenantId = value; return this; }
    public PostgresGatewayEventStoreOptions streamId(String value) { streamId = value; return this; }
    public PostgresGatewayEventStoreOptions pageSize(int value) { pageSize = value; return this; }

    public void validate() {
        if (tenantId == null || tenantId.isBlank()) throw new IllegalArgumentException("tenantId is required");
        if (streamId == null || streamId.isBlank()) throw new IllegalArgumentException("streamId is required");
        if (pageSize < 1) throw new IllegalArgumentException("pageSize must be at least one");
    }
}
