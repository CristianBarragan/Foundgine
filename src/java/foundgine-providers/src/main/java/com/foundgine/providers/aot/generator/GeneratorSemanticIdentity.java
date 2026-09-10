package com.foundgine.providers.aot.generator;

import java.nio.charset.StandardCharsets;

/** Canonical semantic identity and stable hashing rules shared by AOT metadata generation. */
public final class GeneratorSemanticIdentity {
    public static final String ENTITY_NAMESPACE = "entity";
    public static final String FIELD_NAMESPACE = "field";
    public static final String RELATIONSHIP_NAMESPACE = "relationship";
    public static final String TABLE_NAMESPACE = "table";
    public static final String COLUMN_NAMESPACE = "column";
    public static final String MODEL_NAMESPACE = "model";
    public static final String CONNECTION_NAMESPACE = "connection";
    public static final String AUTHORIZATION_NAMESPACE = "authorization";

    private GeneratorSemanticIdentity() {}

    public static String entityKey(String semanticName) { return key(ENTITY_NAMESPACE, semanticName); }
    public static String fieldKey(String entity, String field) { return key(FIELD_NAMESPACE, pair(entity, field)); }
    public static String relationshipKey(String entity, String relationship) { return key(RELATIONSHIP_NAMESPACE, pair(entity, relationship)); }
    public static String tableKey(String storageName) { return key(TABLE_NAMESPACE, storageName); }
    public static String columnKey(String storageName, String columnName) { return key(COLUMN_NAMESPACE, pair(storageName, columnName)); }
    public static String modelKey(String semanticName) { return key(MODEL_NAMESPACE, semanticName); }
    public static String connectionKey(String model, String connection) { return key(CONNECTION_NAMESPACE, pair(model, connection)); }
    public static String authorizationKey(String declaringType, String authorization) { return key(AUTHORIZATION_NAMESPACE, pair(declaringType, authorization)); }

    /** Foundgine's stable 64-bit FNV-1a hash over UTF-8 bytes. */
    public static long hash(String canonicalKey) {
        String normalized = normalize(canonicalKey, "canonicalKey");
        long hash = 0xcbf29ce484222325L;
        for (byte b : normalized.getBytes(StandardCharsets.UTF_8)) {
            hash ^= (b & 0xffL);
            hash *= 0x100000001b3L;
        }
        return hash == 0 ? 1L : hash;
    }

    public static long validateExplicitId(long value, String description) {
        if (value == 0) throw new IllegalArgumentException("Explicit " + description + " identity 0 is reserved and cannot be assigned.");
        return value;
    }

    public static String normalize(String value, String parameterName) {
        if (value == null || value.isBlank()) throw new IllegalArgumentException("Identity component is required: " + parameterName);
        return value.trim();
    }

    private static String key(String namespace, String value) { return namespace + ":" + normalize(value, "value"); }
    private static String pair(String left, String right) { return normalize(left, "left") + "." + normalize(right, "right"); }
}
