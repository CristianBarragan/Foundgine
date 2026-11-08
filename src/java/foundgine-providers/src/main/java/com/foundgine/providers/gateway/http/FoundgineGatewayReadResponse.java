package com.foundgine.providers.gateway.http;

import java.util.List;
import java.util.Map;

/** Public, deliberately minimized read response; runtime evidence is not implicitly serialized. */
public record FoundgineGatewayReadResponse(List<Row> rows, PageInfo pageInfo) {
    /** Values are already reduced to JSON-safe Java values (String, Boolean, Number, List, Map, null). */
    public record Row(Map<String, Object> values) { }
    public record PageInfo(String startCursor, String endCursor, boolean hasNextPage, boolean hasPreviousPage) { }
}
