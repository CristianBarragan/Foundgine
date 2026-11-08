package com.foundgine.extensions.temporal.hosting;

import com.foundgine.extensions.temporal.configuration.FoundgineTemporalOptions;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessInput;
import com.foundgine.extensions.temporal.observability.FoundgineTemporalDiagnostics;
import java.util.Objects;
import java.util.regex.Pattern;

public final class FoundgineWorkflowStarter implements IFoundgineWorkflowStarter {
    private static final Pattern SAFE_SEGMENT = Pattern.compile("^[A-Za-z0-9._:\\-]{1,128}$");

    private final IFoundgineWorkflowClient client;
    private final FoundgineProcessCatalog catalog;
    private final FoundgineTemporalOptions options;

    public FoundgineWorkflowStarter(IFoundgineWorkflowClient client, FoundgineProcessCatalog catalog,
            FoundgineTemporalOptions options) {
        this.client = Objects.requireNonNull(client, "client");
        this.catalog = Objects.requireNonNull(catalog, "catalog");
        this.options = Objects.requireNonNull(options, "options");
    }

    /**
     * Deterministic id: the same tenant/process/business key always maps to the same workflow, which is what makes
     * at-least-once outbox delivery and HTTP retries idempotent. Segments are validated so ids cannot cross tenants.
     */
    @Override
    public String workflowId(String tenantId, String process, String businessKey) {
        check("tenant", tenantId);
        check("process", process);
        check("businessKey", businessKey);
        return options.workflowIdPrefix + "/" + tenantId + "/" + process + "/" + businessKey;
    }

    private static void check(String name, String value) {
        if (value == null || !SAFE_SEGMENT.matcher(value).matches())
            throw new IllegalArgumentException("Invalid " + name
                    + ": 1\u2013128 chars of letters, digits, \u0027.\u0027, \u0027_\u0027, \u0027:\u0027 or \u0027-\u0027.");
    }

    @Override
    public FoundgineWorkflowStartResult start(FoundgineWorkflowStartRequest request) {
        Objects.requireNonNull(request, "request");
        var definition = catalog.tryGet(request.process())
                .orElseThrow(() -> new IllegalArgumentException("Unknown process \u0027" + request.process() + "\u0027."));
        var id = workflowId(request.tenantId(), request.process(), request.businessKey());
        var input = new FoundgineProcessInput(request.tenantId(), request.initiator(), request.businessKey(),
                request.payloadJson(), definition, options.toActivityPolicy(), request.correlationId(),
                request.sourceMessageId());
        boolean started = client.start(id, input);
        FoundgineTemporalDiagnostics.recordStart(request.process(), started ? "started" : "duplicate");
        return new FoundgineWorkflowStartResult(id, !started);
    }
}
