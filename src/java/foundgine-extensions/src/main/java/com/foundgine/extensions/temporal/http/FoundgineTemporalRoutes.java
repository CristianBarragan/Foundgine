package com.foundgine.extensions.temporal.http;

import com.foundgine.core.semantic.security.execution.ISecurityExecutionContextProvider;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.extensions.temporal.contracts.FoundgineApprovalDecision;
import com.foundgine.extensions.temporal.contracts.FoundgineProcessStatus;
import com.foundgine.extensions.temporal.hosting.FoundgineWorkflowStartRequest;
import com.foundgine.extensions.temporal.hosting.IFoundgineWorkflowClient;
import com.foundgine.extensions.temporal.hosting.IFoundgineWorkflowStarter;
import com.foundgine.providers.gateway.http.GatewayHttpAuthorizer;
import com.foundgine.providers.gateway.http.GatewayHttpRequest;
import com.foundgine.providers.gateway.http.GatewayHttpResponse;
import com.foundgine.providers.gateway.http.GatewayHttpRoute;
import com.foundgine.providers.json.FoundgineJson;
import java.io.IOException;
import java.math.BigDecimal;
import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Framework-neutral port of {@code MapFoundgineTemporal}: routes for starting, inspecting, deciding and cancelling
 * processes. Tenant and subject come only from the host {@link ISecurityExecutionContextProvider}; workflow ids are built
 * from the authenticated tenant, so a caller can never address another tenant workflow.
 *
 * <pre>
 * POST {prefix}/{process}                    { "businessKey": "...", "payload": { ... } }  -> 202 (200 when it already existed)
 * GET  {prefix}/{process}/{key}
 * POST {prefix}/{process}/{key}/decision     { "step": "approve", "approved": true, "reason": "..." } -> 202
 * POST {prefix}/{process}/{key}/cancel                                                      -> 202
 * </pre>
 *
 * <p>All routes require authorization. A request whose context has no tenant is rejected with 403 (the C# endpoint
 * silently produces an empty 200 in that case; failing closed is the safer behaviour here).
 */
public final class FoundgineTemporalRoutes {
    private final IFoundgineWorkflowStarter starter;
    private final IFoundgineWorkflowClient client;
    private final ISecurityExecutionContextProvider security;
    private final GatewayHttpAuthorizer authorizer;
    private final FoundgineTemporalHttpOptions http;

    private FoundgineTemporalRoutes(IFoundgineWorkflowStarter starter, IFoundgineWorkflowClient client,
            ISecurityExecutionContextProvider security, GatewayHttpAuthorizer authorizer, FoundgineTemporalHttpOptions http) {
        this.starter = Objects.requireNonNull(starter, "starter");
        this.client = Objects.requireNonNull(client, "client");
        this.security = Objects.requireNonNull(security, "security");
        this.authorizer = Objects.requireNonNull(authorizer, "authorizer");
        this.http = http == null ? new FoundgineTemporalHttpOptions() : http;
    }

    public static List<GatewayHttpRoute> map(IFoundgineWorkflowStarter starter, IFoundgineWorkflowClient client,
            ISecurityExecutionContextProvider security, GatewayHttpAuthorizer authorizer, FoundgineTemporalHttpOptions http) {
        var routes = new FoundgineTemporalRoutes(starter, client, security, authorizer, http);
        var prefix = routes.http.routePrefix.endsWith("/")
                ? routes.http.routePrefix.substring(0, routes.http.routePrefix.length() - 1) : routes.http.routePrefix;
        var list = new ArrayList<GatewayHttpRoute>();
        list.add(new GatewayHttpRoute("POST", prefix + "/{process}", true, routes::start));
        list.add(new GatewayHttpRoute("GET", prefix + "/{process}/{key}", true, routes::status));
        list.add(new GatewayHttpRoute("POST", prefix + "/{process}/{key}/cancel", true, routes::cancel));
        list.add(new GatewayHttpRoute("POST", prefix + "/{process}/{key}/decision", true, routes::decide));
        return List.copyOf(list);
    }

    // ------------------------------------------------------------------------------------------------- handlers

    private void start(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var ctx = context(res); if (ctx == null) return;
        var body = readObject(req, res, http.maxPayloadBytes + 4096); if (body == null) return;
        if (!(body.get("businessKey") instanceof String key) || key.isBlank()) { error(res, 400, "businessKey is required."); return; }
        var sb = new StringBuilder();
        writeValue(sb, body.containsKey("payload") ? body.get("payload") : new java.util.LinkedHashMap<String, Object>());
        var payload = sb.toString();
        if (payload.getBytes(StandardCharsets.UTF_8).length > http.maxPayloadBytes) { error(res, 413, "Payload too large."); return; }
        try {
            var result = starter.start(new FoundgineWorkflowStartRequest(ctx.tenant(), ctx.subject(),
                    req.pathParam("process"), key, payload, req.header("X-Correlation-Id"), null));
            res.status(result.alreadyExisted() ? 200 : 202);
            res.header("Location", req.path() + "/" + key);
            var out = new StringBuilder("{\"workflowId\":");
            FoundgineJson.writeString(out, result.workflowId());
            out.append(",\"status\":").append(result.alreadyExisted() ? "\"Existing\"" : "\"Started\"")
                    .append(",\"alreadyExisted\":").append(result.alreadyExisted()).append('}');
            res.writeJson(out.toString());
        } catch (IllegalArgumentException ex) {
            error(res, 400, ex.getMessage());
        }
    }

    private void status(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var id = workflowId(req, res); if (id == null) return;
        var description = client.describe(id);
        if (description == null) { error(res, 404, "Workflow not found."); return; }
        var out = new StringBuilder("{\"workflowId\":");
        FoundgineJson.writeString(out, id);
        out.append(",\"temporalStatus\":");
        FoundgineJson.writeString(out, description.status().displayName());
        out.append(",\"process\":");
        writeProcess(out, description.process());
        out.append('}');
        res.status(200);
        res.writeJson(out.toString());
    }

    private void cancel(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var id = workflowId(req, res); if (id == null) return;
        if (!client.cancel(id)) { error(res, 404, "Workflow not found."); return; }
        res.status(202);
    }

    private void decide(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        if (http.approvalPolicy != null && !http.approvalPolicy.isBlank()
                && !authorizer.satisfiesPolicy(req, http.approvalPolicy)) { res.status(403); return; }
        var ctx = context(res); if (ctx == null) return;
        var id = workflowId(req, res); if (id == null) return;
        var body = readObject(req, res, 16 * 1024); if (body == null) return;
        if (!(body.get("step") instanceof String step) || step.isBlank()) { error(res, 400, "Step is required."); return; }
        boolean approved = Boolean.TRUE.equals(body.get("approved"));
        var reason = body.get("reason") instanceof String r ? r : null;
        // DecidedBy is stamped from the trusted context, never from the request body.
        if (!client.signalDecision(id, new FoundgineApprovalDecision(step, approved, reason, ctx.subject()))) {
            error(res, 404, "Workflow not found.");
            return;
        }
        res.status(202);
    }

    // ----------------------------------------------------------------------------------------------- plumbing

    /** The trusted context with a tenant, or null after writing 401/403. */
    private SecurityExecutionContext context(GatewayHttpResponse res) throws IOException {
        var ctx = security.getSecurityExecutionContext();
        if (ctx == null) { res.status(401); return null; }
        if (ctx.tenant() == null || ctx.tenant().isBlank()) { error(res, 403, "A tenant is required."); return null; }
        return ctx;
    }

    private String workflowId(GatewayHttpRequest req, GatewayHttpResponse res) throws IOException {
        var ctx = context(res); if (ctx == null) return null;
        try {
            return starter.workflowId(ctx.tenant(), req.pathParam("process"), req.pathParam("key"));
        } catch (IllegalArgumentException ex) {
            error(res, 400, ex.getMessage());
            return null;
        }
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> readObject(GatewayHttpRequest req, GatewayHttpResponse res, int limit) throws IOException {
        if (req.contentLength() > limit) { error(res, 413, "Payload too large."); return null; }
        var bytes = req.body().readNBytes(limit + 1);
        if (bytes.length > limit) { error(res, 413, "Payload too large."); return null; }
        if (bytes.length == 0) { error(res, 400, "Request body is required."); return null; }
        try {
            var value = FoundgineJson.parse(new String(bytes, StandardCharsets.UTF_8));
            if (value instanceof Map<?, ?> m) return (Map<String, Object>) m;
        } catch (IllegalArgumentException ex) {
            // fall through: malformed JSON
        }
        error(res, 400, "Request body must be a JSON object.");
        return null;
    }

    private static void error(GatewayHttpResponse res, int status, String message) throws IOException {
        res.status(status);
        var sb = new StringBuilder("{\"error\":");
        FoundgineJson.writeString(sb, message == null ? "Error." : message);
        sb.append('}');
        res.writeJson(sb.toString());
    }

    private static void writeProcess(StringBuilder sb, FoundgineProcessStatus p) {
        if (p == null) { sb.append("null"); return; }
        sb.append("{\"process\":"); FoundgineJson.writeString(sb, p.process());
        sb.append(",\"state\":"); FoundgineJson.writeString(sb, p.state());
        sb.append(",\"currentStep\":"); FoundgineJson.writeNullableString(sb, p.currentStep());
        sb.append(",\"completedSteps\":"); writeList(sb, p.completedSteps());
        sb.append(",\"compensatedSteps\":"); writeList(sb, p.compensatedSteps());
        sb.append(",\"pendingApprovalStep\":"); FoundgineJson.writeNullableString(sb, p.pendingApprovalStep());
        sb.append(",\"failure\":"); FoundgineJson.writeNullableString(sb, p.failure());
        sb.append('}');
    }

    private static void writeList(StringBuilder sb, List<String> items) {
        sb.append('[');
        for (int i = 0; i < items.size(); i++) {
            if (i > 0) sb.append(',');
            FoundgineJson.writeString(sb, items.get(i));
        }
        sb.append(']');
    }

    /** Re-serializes a parsed JSON value (see {@link FoundgineJson#parse}); numbers keep their exact form. */
    static void writeValue(StringBuilder sb, Object value) {
        if (value == null) sb.append("null");
        else if (value instanceof String s) FoundgineJson.writeString(sb, s);
        else if (value instanceof Boolean || value instanceof Long || value instanceof BigInteger) sb.append(value);
        else if (value instanceof BigDecimal d) sb.append(d.toString());
        else if (value instanceof Map<?, ?> m) {
            sb.append('{');
            boolean first = true;
            for (var e : m.entrySet()) {
                if (!first) sb.append(',');
                first = false;
                FoundgineJson.writeString(sb, String.valueOf(e.getKey()));
                sb.append(':');
                writeValue(sb, e.getValue());
            }
            sb.append('}');
        } else if (value instanceof List<?> l) {
            sb.append('[');
            for (int i = 0; i < l.size(); i++) {
                if (i > 0) sb.append(',');
                writeValue(sb, l.get(i));
            }
            sb.append(']');
        } else throw new IllegalArgumentException("Unsupported JSON value: " + value.getClass().getName());
    }
}
