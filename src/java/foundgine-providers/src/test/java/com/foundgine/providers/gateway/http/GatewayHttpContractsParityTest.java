package com.foundgine.providers.gateway.http;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionPageInfo;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.capabilities.SemanticCapabilityContract;
import com.foundgine.core.semantic.intent.*;
import com.foundgine.core.semantic.mutation.SemanticMutationKind;
import com.foundgine.core.semantic.query.*;
import com.foundgine.providers.gateway.semantics.FoundgineGatewaySemanticSource;
import com.foundgine.runtime.IFoundgine;
import java.lang.reflect.Proxy;
import java.math.BigDecimal;
import java.time.Instant;
import java.util.*;
import org.junit.jupiter.api.Test;

class GatewayHttpContractsParityTest {
    private static final FoundgineGatewayHttpOptions LIMITS = new FoundgineGatewayHttpOptions();

    private static ReadIntent read(String json) {
        return FoundgineGatewayReadRequestMapper.toIntent(FoundgineGatewayReadRequest.fromJson(json), LIMITS);
    }

    @Test void readRequestMapsSelectionsFilterOrderAndAppliesDefaultLimit() {
        var intent = read("""
                {"rootEntity":"Customer","selections":[{"field":"Name"},{"relationship":"Orders","children":[{"field":"Total"}]}],
                 "filter":{"kind":"and","expressions":[
                    {"kind":"field","field":"Age","operator":"gte","value":18},
                    {"kind":"relationship","relationship":"Orders","quantifier":"Some","predicate":{"kind":"field","field":"Total","operator":"GT","value":12.50}}]},
                 "order":[{"field":"Name","direction":"desc"}],"offset":5,"after":"abc"}""");
        assertEquals("Customer", intent.rootEntity());
        assertEquals(2, intent.selections().size());
        assertEquals("Total", intent.selections().get(1).children().get(0).field());
        assertEquals(100, (int) intent.limit());
        assertEquals(5, (int) intent.offset());
        assertEquals("abc", intent.after());
        var and = (ReadAndFilter) intent.filter();
        var age = (ReadFieldFilter) and.expressions().get(0);
        assertEquals(SemanticFilterOperator.GTE, age.operator());
        assertEquals(18L, age.value());
        var rel = (ReadRelationshipFilter) and.expressions().get(1);
        assertEquals(SemanticRelationshipQuantifier.SOME, rel.quantifier());
        assertEquals(new BigDecimal("12.50"), ((ReadFieldFilter) rel.predicate()).value());
        assertEquals(SemanticSortDirection.DESC, intent.order().get(0).direction());
        assertEquals(SemanticOrderAggregate.NONE, intent.order().get(0).aggregate());
    }

    @Test void readRequestRejectsInvalidShapesAndBoundsExceeded() {
        String sel = "\"selections\":[{\"field\":\"A\"}]";
        assertThrows(IllegalArgumentException.class, () -> read("{" + sel + "}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\",\"selections\":[{\"field\":\"A\",\"relationship\":\"R\"}]}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\",\"selections\":[{}]}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"limit\":0}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"limit\":1001}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"offset\":-1}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"after\":\"" + "x".repeat(4097) + "\"}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"surprise\":1}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"filter\":{\"kind\":\"nope\"}}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"filter\":{\"kind\":\"or\",\"expressions\":[]}}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"filter\":{\"kind\":\"field\",\"field\":\"A\",\"operator\":\"like\",\"value\":1}}"));
        assertThrows(IllegalArgumentException.class, () -> read("{\"rootEntity\":\"E\"," + sel + ",\"filter\":{\"kind\":\"relationship\",\"relationship\":\"R\",\"quantifier\":\"all\"}}"));
        var small = new FoundgineGatewayHttpOptions(); small.maxSelectionCount = 1;
        var req = FoundgineGatewayReadRequest.fromJson("{\"rootEntity\":\"E\",\"selections\":[{\"field\":\"A\"},{\"field\":\"B\"}]}");
        assertThrows(IllegalArgumentException.class, () -> FoundgineGatewayReadRequestMapper.toIntent(req, small));
        var shallow = new FoundgineGatewayHttpOptions(); shallow.maxJsonValueDepth = 1;
        var deep = FoundgineGatewayReadRequest.fromJson("{\"rootEntity\":\"E\"," + sel + ",\"filter\":{\"kind\":\"field\",\"field\":\"A\",\"operator\":\"in\",\"value\":[[1]]}}");
        assertThrows(IllegalArgumentException.class, () -> FoundgineGatewayReadRequestMapper.toIntent(deep, shallow));
    }

    @Test void readResponseReducesValuesToJsonSafeTypesAndSerializes() {
        var id = UUID.fromString("11111111-2222-3333-4444-555555555555");
        var values = new LinkedHashMap<String, Object>();
        values.put("id", id); values.put("n", 5); values.put("price", new BigDecimal("1.50")); values.put("at", Instant.parse("2026-11-08T10:00:00Z"));
        values.put("bin", new byte[] {1, 2, 3}); values.put("tags", List.of("a", "b\"")); values.put("none", null); values.put("kind", SemanticMutationKind.UPSERT);
        var result = new ExecutionResult(List.of(new ExecutionRow(values)), new ExecutionPageInfo(null, "end", true, false), null, null);
        String json = FoundgineGatewayReadResponseMapper.toJson(FoundgineGatewayReadResponseMapper.map(result));
        assertEquals("{\"rows\":[{\"values\":{\"id\":\"11111111-2222-3333-4444-555555555555\",\"n\":5,\"price\":1.50,"
                + "\"at\":\"2026-11-08T10:00:00Z\",\"bin\":\"AQID\",\"tags\":[\"a\",\"b\\\"\"],\"none\":null,\"kind\":\"UPSERT\"}}],"
                + "\"pageInfo\":{\"startCursor\":null,\"endCursor\":\"end\",\"hasNextPage\":true,\"hasPreviousPage\":false}}", json);
        assertEquals("{\"rows\":[],\"pageInfo\":null}", FoundgineGatewayReadResponseMapper.toJson(
                FoundgineGatewayReadResponseMapper.map(new ExecutionResult(List.of()))));
        var bad = new ExecutionResult(List.of(new ExecutionRow(Map.of("x", new Object()))));
        assertThrows(UnsupportedOperationException.class, () -> FoundgineGatewayReadResponseMapper.map(bad));
        var nan = new ExecutionResult(List.of(new ExecutionRow(Map.of("x", Double.NaN))));
        assertThrows(UnsupportedOperationException.class, () -> FoundgineGatewayReadResponseMapper.map(nan));
    }

    private static com.foundgine.core.semantic.mutation.SemanticMutationOperationGraph mutation(String json) {
        return FoundgineGatewayMutationRequestMapper.toGraph(FoundgineGatewayMutationRequest.fromJson(json), LIMITS);
    }

    @Test void mutationRequestMapsCanonicalGraphWithUnsignedIds() {
        var graph = mutation("""
                {"operations":[
                  {"entityId":18446744073709551615,"kind":"create","fields":{"7":"x","9":null},"returnFields":[7]},
                  {"entityId":5,"kind":"Update","fields":{"7":1},"filter":{"kind":"and","expressions":[
                      {"kind":"field","fieldId":7,"operator":"eq","value":1},{"kind":"field","fieldId":9,"operator":"in","value":[1,2]}]}},
                  {"entityId":5,"kind":"upsert","fields":{"7":"k"},"conflictFields":[7]},
                  {"entityId":5,"kind":"delete","filter":{"kind":"field","fieldId":7,"operator":"eq","value":"z"}}]}""");
        assertEquals(4, graph.operations().size());
        assertEquals(new EntityId(-1L), graph.operations().get(0).entity());
        assertEquals(SemanticMutationKind.CREATE, graph.operations().get(0).kind());
        assertEquals(2, graph.operations().get(0).fields().size());
        assertEquals(List.of(new FieldId(7)), graph.operations().get(0).returnFields());
        assertEquals(SemanticMutationKind.UPSERT, graph.operations().get(2).kind());
    }

    @Test void mutationRequestEnforcesKindSpecificRulesAndLimits() {
        String f = "\"filter\":{\"kind\":\"field\",\"fieldId\":7,\"operator\":\"eq\",\"value\":1}";
        assertThrows(IllegalArgumentException.class, () -> mutation("{}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":0,\"kind\":\"create\"}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"merge\"}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"update\",\"fields\":{\"7\":1}}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"create\"," + f + "}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"upsert\",\"fields\":{\"7\":1}}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"update\",\"conflictFields\":[7],\"fields\":{\"7\":1}," + f + "}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"delete\",\"fields\":{\"7\":1}," + f + "}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"create\",\"fields\":{\"abc\":1}}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"create\",\"fields\":{\"0\":1}}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"create\",\"fields\":{\"-5\":1}}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"create\",\"returnFields\":[0]}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":-1,\"kind\":\"create\"}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":18446744073709551616,\"kind\":\"create\"}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"update\",\"fields\":{\"7\":1},\"filter\":{\"kind\":\"relationship\"}}]}"));
        assertThrows(IllegalArgumentException.class, () -> mutation("{\"operations\":[{\"entityId\":1,\"kind\":\"create\",\"extra\":1}]}"));
        var one = new FoundgineGatewayHttpOptions(); one.maxMutationOperations = 1;
        var two = FoundgineGatewayMutationRequest.fromJson("{\"operations\":[{\"entityId\":1,\"kind\":\"create\"},{\"entityId\":1,\"kind\":\"create\"}]}");
        assertThrows(IllegalArgumentException.class, () -> FoundgineGatewayMutationRequestMapper.toGraph(two, one));
    }

    @Test void semanticSourceDelegatesToIFoundgineAndRejectsNull() {
        var contract = new SemanticCapabilityContract(1, List.of());
        var foundgine = (IFoundgine) Proxy.newProxyInstance(getClass().getClassLoader(), new Class<?>[] {IFoundgine.class}, (p, m, a) -> switch (m.getName()) {
            case "describeCapabilityContract" -> contract;
            case "describeVersionSet" -> null;
            default -> throw new UnsupportedOperationException(m.getName());
        });
        var description = new FoundgineGatewaySemanticSource(foundgine).describe();
        assertEquals(contract, description.contract());
        assertNull(description.versions());
        assertThrows(IllegalArgumentException.class, () -> new FoundgineGatewaySemanticSource(null));
    }
}
