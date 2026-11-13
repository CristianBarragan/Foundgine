package com.foundgine.providers.tools.mcp;

import static com.foundgine.providers.tools.mcp.McpAdapterParityTest.host;
import static org.junit.jupiter.api.Assertions.*;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.execution.ExecutionEvidence;
import com.foundgine.core.execution.ExecutionPageInfo;
import com.foundgine.core.execution.ExecutionReceiptFactory;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.runtime.IFoundgine;
import java.lang.reflect.Proxy;
import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.Test;

/** C# serializes rows/pageInfo/evidence/receipt with JsonSerializerDefaults.Web; a real receipt carries timestamps. */
class McpResultSerializationParityTest {
    @Test
    void realReceiptEvidenceAndPageInfoSerializeAsCamelCaseWithIsoTimestamps() throws Exception {
        var started = Instant.parse("2026-01-01T00:00:00Z");
        var evidence = new ExecutionEvidence("sql", "plan-fp", List.of(1, 2), 1, 4, null, "intent-fp", "auth-fp", null, null, null, null);
        var receipt = ExecutionReceiptFactory.create("req-1", evidence, "result-fp", List.of(2, 1), List.of("read"),
                started, started.plusMillis(4), 1, 1, 1, 1, "model-1");
        var result = new ExecutionResult(List.of(new ExecutionRow(Map.of("id", 7L, "name", "Acme"))),
                new ExecutionPageInfo("a", "b", true, false), evidence, receipt);
        IFoundgine engine = (IFoundgine) Proxy.newProxyInstance(IFoundgine.class.getClassLoader(), new Class<?>[] {IFoundgine.class},
                (p, m, a) -> {
                    if (m.getName().equals("executeAsync") && a.length == 3) return CompletableFuture.completedFuture(result);
                    throw new UnsupportedOperationException(m.getName());
                });
        var tools = FoundgineMcpTools.builder(engine).securityContextFactory(() -> host("alice")).build();

        var json = tools.executeSemanticQueryAsync("{\"rootEntity\":\"Order\",\"selections\":[{\"field\":\"id\"}]}")
                .toCompletableFuture().get();
        var node = new ObjectMapper().readTree(json);

        assertEquals("Acme", node.get("rows").get(0).get("name").asText());
        assertTrue(node.get("pageInfo").get("hasNextPage").asBoolean());
        assertEquals("plan-fp", node.get("evidence").get("planFingerprint").asText());
        var r = node.get("receipt");
        assertEquals("req-1", r.get("requestId").asText());
        assertEquals("succeeded", r.get("status").asText());
        assertEquals("2026-01-01T00:00:00Z", r.get("startedAt").asText());
        assertEquals("result-fp", r.get("resultFingerprint").asText());
    }
}
