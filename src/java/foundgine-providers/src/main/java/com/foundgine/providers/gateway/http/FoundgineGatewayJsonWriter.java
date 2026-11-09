package com.foundgine.providers.gateway.http;

import com.foundgine.providers.gateway.FoundgineGatewayCapability;
import com.foundgine.providers.gateway.FoundgineGatewayDescription;
import com.foundgine.providers.gateway.execution.FoundgineGatewayExecutionStatus;
import com.foundgine.providers.json.FoundgineJson;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

/**
 * Reflection-free writers for the gateway's small response contracts. Property names are camelCase in record
 * declaration order and nulls are written explicitly, matching the .NET {@code FoundgineGatewayJsonContext}.
 */
public final class FoundgineGatewayJsonWriter {
    private FoundgineGatewayJsonWriter() { }

    public static String description(FoundgineGatewayDescription d) {
        var sb = new StringBuilder(256);
        sb.append("{\"name\":"); FoundgineJson.writeString(sb, d.name());
        sb.append(",\"contractVersion\":"); FoundgineJson.writeString(sb, d.contractVersion());
        sb.append(",\"capabilities\":[");
        boolean first = true;
        for (var c : d.capabilities()) { if (!first) sb.append(','); first = false; capability(sb, c); }
        return sb.append("]}").toString();
    }

    public static String capability(FoundgineGatewayCapability c) {
        var sb = new StringBuilder(128);
        capability(sb, c);
        return sb.toString();
    }

    private static void capability(StringBuilder sb, FoundgineGatewayCapability c) {
        sb.append("{\"id\":"); FoundgineJson.writeString(sb, c.id());
        sb.append(",\"name\":"); FoundgineJson.writeString(sb, c.name());
        sb.append(",\"version\":"); FoundgineJson.writeString(sb, c.version());
        sb.append(",\"kind\":"); FoundgineJson.writeString(sb, c.kind());
        sb.append(",\"description\":"); FoundgineJson.writeString(sb, c.description());
        sb.append(",\"operations\":"); stringArray(sb, c.operations());
        // Map.copyOf loses insertion order, so keys are sorted to keep the output deterministic.
        sb.append(",\"metadata\":"); FoundgineJson.writeStringMap(sb, new TreeMap<>(c.metadata()));
        sb.append('}');
    }

    public static String executionStatus(FoundgineGatewayExecutionStatus s) {
        var sb = new StringBuilder(160);
        sb.append("{\"executionId\":"); FoundgineJson.writeString(sb, s.executionId());
        sb.append(",\"status\":"); FoundgineJson.writeString(sb, s.status());
        sb.append(",\"operationKind\":"); FoundgineJson.writeString(sb, s.operationKind());
        sb.append(",\"startedAt\":"); FoundgineJson.writeNullableString(sb, s.startedAt() == null ? null : s.startedAt().toString());
        sb.append(",\"completedAt\":"); FoundgineJson.writeNullableString(sb, s.completedAt() == null ? null : s.completedAt().toString());
        sb.append(",\"outcomeCode\":"); FoundgineJson.writeNullableString(sb, s.outcomeCode());
        return sb.append('}').toString();
    }

    public static String dryRun(FoundgineGatewayMutationResponses.DryRun r) {
        var sb = new StringBuilder(256);
        sb.append("{\"planFingerprint\":"); FoundgineJson.writeNullableString(sb, r.planFingerprint());
        sb.append(",\"operations\":[");
        boolean first = true;
        for (var op : r.operations()) {
            if (!first) sb.append(','); first = false;
            sb.append("{\"index\":").append(op.index());
            sb.append(",\"entity\":"); FoundgineJson.writeNullableString(sb, op.entity());
            sb.append(",\"kind\":"); FoundgineJson.writeNullableString(sb, op.kind());
            sb.append(",\"fields\":"); stringArray(sb, op.fields());
            sb.append(",\"returnFields\":"); stringArray(sb, op.returnFields());
            sb.append('}');
        }
        sb.append("],\"effects\":"); stringArray(sb, r.effects());
        return sb.append('}').toString();
    }

    public static String approval(FoundgineGatewayMutationResponses.Approval a) {
        var sb = new StringBuilder(160);
        sb.append("{\"handle\":"); FoundgineJson.writeString(sb, a.handle());
        sb.append(",\"expiresAt\":"); FoundgineJson.writeString(sb, a.expiresAt().toString());
        sb.append(",\"planFingerprint\":"); FoundgineJson.writeNullableString(sb, a.planFingerprint());
        return sb.append('}').toString();
    }

    public static String execution(FoundgineGatewayMutationResponses.Execution e) {
        var sb = new StringBuilder(192);
        sb.append("{\"planFingerprint\":"); FoundgineJson.writeNullableString(sb, e.planFingerprint());
        sb.append(",\"resultFingerprint\":"); FoundgineJson.writeNullableString(sb, e.resultFingerprint());
        sb.append(",\"approvalId\":"); FoundgineJson.writeNullableString(sb, e.approvalId());
        sb.append(",\"approvedBy\":"); FoundgineJson.writeNullableString(sb, e.approvedBy());
        sb.append(",\"operationCount\":").append(e.operationCount());
        sb.append(",\"totalAffectedRows\":").append(e.totalAffectedRows());
        return sb.append('}').toString();
    }

    private static void stringArray(StringBuilder sb, List<String> values) {
        sb.append('[');
        if (values != null) {
            boolean first = true;
            for (var v : values) { if (!first) sb.append(','); first = false; FoundgineJson.writeNullableString(sb, v); }
        }
        sb.append(']');
    }
}
