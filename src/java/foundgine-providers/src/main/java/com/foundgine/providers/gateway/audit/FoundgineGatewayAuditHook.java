package com.foundgine.providers.gateway.audit;

import com.foundgine.providers.gateway.FoundgineGatewayEvent;
import com.foundgine.providers.gateway.IFoundgineGatewayCriticalHook;
import java.util.Objects;

/** Forwards gateway events to a durable audit sink before journal append and live subscriber delivery. */
public final class FoundgineGatewayAuditHook implements IFoundgineGatewayCriticalHook {
    private final IFoundgineGatewayAuditSink sink;
    public FoundgineGatewayAuditHook(IFoundgineGatewayAuditSink sink) { this.sink = Objects.requireNonNull(sink, "sink"); }
    @Override public void onEvent(FoundgineGatewayEvent gatewayEvent) throws Exception { sink.append(gatewayEvent); }
}
