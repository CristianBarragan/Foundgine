package com.foundgine.providers.gateway.http;

import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.providers.gateway.IFoundgineGateway;
import com.foundgine.providers.gateway.IFoundgineGatewayEventPublisher;
import com.foundgine.providers.gateway.approvals.FoundgineGatewayApprovalService;
import com.foundgine.providers.gateway.events.FoundgineGatewayEventHub;
import com.foundgine.providers.gateway.events.FoundgineGatewayEventStreamOptions;
import com.foundgine.providers.gateway.events.FoundgineGatewayScopeDigester;
import com.foundgine.providers.gateway.events.IFoundgineGatewayEventStore;
import com.foundgine.providers.gateway.execution.IFoundgineGatewayExecution;
import com.foundgine.providers.gateway.semantics.FoundgineGatewaySemanticDescription;
import com.foundgine.providers.gateway.semantics.IFoundgineGatewaySemanticSource;
import java.util.Objects;
import java.util.function.Function;

/**
 * Everything the gateway endpoints need, wired explicitly (the Java counterpart of the services the ASP.NET handlers
 * resolve from {@code RequestServices}). Use {@link #builder()}; there is deliberately no DI container.
 */
public final class FoundgineGatewayHttpServices {
    final IFoundgineGateway gateway;
    final IFoundgineGatewaySemanticSource semanticSource;
    final Function<FoundgineGatewaySemanticDescription, String> semanticJson;
    final IFoundgineGatewayExecution execution;
    final FoundgineGatewayApprovalService approvals;
    final FoundgineGatewayEventHub hub;
    final IFoundgineGatewayEventStore eventStore;
    final IFoundgineGatewayEventPublisher publisher;
    final FoundgineGatewayScopeDigester digester;
    final FoundgineGatewayEventStreamOptions streamOptions;
    final FoundgineGatewayHttpOptions limits;
    final ISecurityExecutionContextProvider security;
    final GatewayHttpAuthorizer authorizer;

    private FoundgineGatewayHttpServices(Builder b) {
        gateway = Objects.requireNonNull(b.gateway, "gateway");
        semanticSource = Objects.requireNonNull(b.semanticSource, "semanticSource");
        semanticJson = b.semanticJson == null ? FoundgineGatewaySemanticJson::toJson : b.semanticJson;
        execution = Objects.requireNonNull(b.execution, "execution");
        approvals = Objects.requireNonNull(b.approvals, "approvals");
        hub = Objects.requireNonNull(b.hub, "hub");
        eventStore = b.eventStore == null ? hub.eventStore() : b.eventStore;
        publisher = b.publisher == null ? hub : b.publisher;
        streamOptions = Objects.requireNonNull(b.streamOptions, "streamOptions");
        streamOptions.validate();
        digester = b.digester == null ? new FoundgineGatewayScopeDigester(streamOptions) : b.digester;
        limits = b.limits == null ? new FoundgineGatewayHttpOptions() : b.limits;
        limits.validateReadLimits();
        security = Objects.requireNonNull(b.security, "security");
        authorizer = b.authorizer;
    }

    public GatewayHttpAuthorizer authorizer() { return authorizer; }

    public static Builder builder() { return new Builder(); }

    public static final class Builder {
        private IFoundgineGateway gateway;
        private IFoundgineGatewaySemanticSource semanticSource;
        private Function<FoundgineGatewaySemanticDescription, String> semanticJson;
        private IFoundgineGatewayExecution execution;
        private FoundgineGatewayApprovalService approvals;
        private FoundgineGatewayEventHub hub;
        private IFoundgineGatewayEventStore eventStore;
        private IFoundgineGatewayEventPublisher publisher;
        private FoundgineGatewayScopeDigester digester;
        private FoundgineGatewayEventStreamOptions streamOptions = new FoundgineGatewayEventStreamOptions();
        private FoundgineGatewayHttpOptions limits;
        private ISecurityExecutionContextProvider security;
        private GatewayHttpAuthorizer authorizer;

        public Builder gateway(IFoundgineGateway v) { gateway = v; return this; }
        public Builder semanticSource(IFoundgineGatewaySemanticSource v) { semanticSource = v; return this; }
        /** Overrides how the semantic description is written (default: Jackson over the core contract types). */
        public Builder semanticJson(Function<FoundgineGatewaySemanticDescription, String> v) { semanticJson = v; return this; }
        public Builder execution(IFoundgineGatewayExecution v) { execution = v; return this; }
        public Builder approvals(FoundgineGatewayApprovalService v) { approvals = v; return this; }
        /** The hub is also the default event publisher and the source of the default event store. */
        public Builder hub(FoundgineGatewayEventHub v) { hub = v; return this; }
        public Builder eventStore(IFoundgineGatewayEventStore v) { eventStore = v; return this; }
        public Builder publisher(IFoundgineGatewayEventPublisher v) { publisher = v; return this; }
        public Builder scopeDigester(FoundgineGatewayScopeDigester v) { digester = v; return this; }
        public Builder streamOptions(FoundgineGatewayEventStreamOptions v) { streamOptions = v; return this; }
        public Builder httpOptions(FoundgineGatewayHttpOptions v) { limits = v; return this; }
        public Builder security(ISecurityExecutionContextProvider v) { security = v; return this; }
        public Builder authorizer(GatewayHttpAuthorizer v) { authorizer = v; return this; }

        public FoundgineGatewayHttpServices build() { return new FoundgineGatewayHttpServices(this); }
    }
}
