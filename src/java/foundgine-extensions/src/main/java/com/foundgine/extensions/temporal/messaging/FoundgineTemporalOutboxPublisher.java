package com.foundgine.extensions.temporal.messaging;

import com.foundgine.extensions.temporal.configuration.FoundgineTemporalOptions;
import com.foundgine.extensions.temporal.configuration.UnroutedTopicBehavior;
import com.foundgine.extensions.temporal.hosting.FoundgineWorkflowStartRequest;
import com.foundgine.extensions.temporal.hosting.IFoundgineWorkflowStarter;
import com.foundgine.providers.outbox.FoundgineOutboxMessage;
import com.foundgine.providers.outbox.IFoundgineOutboxPublisher;
import java.util.Objects;

/**
 * Bridges the Foundgine transactional outbox to the workflow engine. Register it as the dispatcher publisher. The
 * outbox message id becomes the workflow business key, so redelivery (the outbox is at-least-once) maps to the same
 * workflow id and is absorbed instead of duplicated.
 */
public final class FoundgineTemporalOutboxPublisher implements IFoundgineOutboxPublisher {
    public static final String INITIATOR_HEADER = "initiator";

    private final IFoundgineWorkflowStarter starter;
    private final FoundgineTemporalOptions options;

    public FoundgineTemporalOutboxPublisher(IFoundgineWorkflowStarter starter, FoundgineTemporalOptions options) {
        this.starter = Objects.requireNonNull(starter, "starter");
        this.options = Objects.requireNonNull(options, "options");
    }

    @Override
    public void publish(FoundgineOutboxMessage message) {
        var process = options.topicRoutes.get(message.topic());
        if (process == null) {
            if (options.unroutedTopicBehavior == UnroutedTopicBehavior.FAIL)
                throw new IllegalStateException("No Temporal process is routed for outbox topic \u0027" + message.topic() + "\u0027.");
            return;
        }
        var header = message.headers() == null ? null : message.headers().get(INITIATOR_HEADER);
        var initiator = header != null && !header.isBlank() ? header : "system:outbox";
        starter.start(new FoundgineWorkflowStartRequest(message.tenantId(), initiator, process,
                message.id().toString().replace("-", ""), message.payloadJson(), message.correlationId(), message.id()));
    }
}
