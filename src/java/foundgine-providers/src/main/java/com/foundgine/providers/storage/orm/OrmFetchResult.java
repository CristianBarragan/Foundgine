package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Provider read result with semantic rows and a separate, non-projectable channel for ordering
 * values. Each private row contains one value for each aggregate order term, in request order.
 */
public record OrmFetchResult(
        List<Map<FieldId, Object>> rows,
        List<List<Object>> privateOrderValues) {
    public OrmFetchResult {
        var rowCopy = new ArrayList<Map<FieldId, Object>>(rows.size());
        for (var row : rows) rowCopy.add(Collections.unmodifiableMap(new LinkedHashMap<>(row)));
        rows = List.copyOf(rowCopy);
        var privateCopy = new ArrayList<List<Object>>(privateOrderValues.size());
        for (var values : privateOrderValues)
            privateCopy.add(Collections.unmodifiableList(new ArrayList<>(values)));
        privateOrderValues = List.copyOf(privateCopy);
        if (!privateOrderValues.isEmpty() && privateOrderValues.size() != rows.size())
            throw new IllegalArgumentException("Private ordering values must align one-to-one with result rows.");
    }

    public static OrmFetchResult semanticOnly(List<Map<FieldId, Object>> rows) {
        return new OrmFetchResult(rows, List.of());
    }
}
