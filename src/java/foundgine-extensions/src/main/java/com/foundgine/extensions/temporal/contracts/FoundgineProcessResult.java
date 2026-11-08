package com.foundgine.extensions.temporal.contracts;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

public record FoundgineProcessResult(String outcome, Map<String, String> outputs, String failure) {
    public FoundgineProcessResult {
        outputs = Collections.unmodifiableMap(new LinkedHashMap<>(outputs == null ? Map.of() : outputs));
    }

    public FoundgineProcessResult(String outcome, Map<String, String> outputs) { this(outcome, outputs, null); }
}
