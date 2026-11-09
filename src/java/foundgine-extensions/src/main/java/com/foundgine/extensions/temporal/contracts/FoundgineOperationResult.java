package com.foundgine.extensions.temporal.contracts;

public record FoundgineOperationResult(String outputJson) {
    public FoundgineOperationResult() { this(null); }
}
