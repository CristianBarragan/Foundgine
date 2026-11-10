package com.foundgine.providers.storage.orm;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayList;
import java.util.Base64;
import java.util.HexFormat;
import java.util.List;
import java.util.Objects;

/**
 * Versioned aggregate-cursor envelope bound to a canonical semantic query-plan identity.
 *
 * <p>The fingerprint prevents accidental cross-plan cursor replay; it is not a signature and
 * must not be treated as an authenticity or authorization mechanism. Every page must still
 * rebuild and apply its current authorization predicates.</p>
 */
public final class OrmAggregateCursorBinding {
    private static final ObjectMapper MAPPER = new ObjectMapper();
    public static final int CURSOR_SCHEMA_VERSION = 1;
    private static final int VERSION = CURSOR_SCHEMA_VERSION;

    private OrmAggregateCursorBinding() {}

    /** SHA-256 fingerprint of the caller's canonical, authorization-inclusive plan identity. */
    public static String fingerprint(String canonicalPlanIdentity) {
        Objects.requireNonNull(canonicalPlanIdentity, "canonicalPlanIdentity");
        if (canonicalPlanIdentity.isBlank())
            throw new IllegalArgumentException("Canonical plan identity cannot be blank.");
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256")
                    .digest(canonicalPlanIdentity.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 is unavailable.", e);
        }
    }

    /** Encodes private aggregate-order values together with their query-plan fingerprint. */
    public static String encode(String planFingerprint, List<?> values) {
        validateFingerprint(planFingerprint);
        Objects.requireNonNull(values, "values");
        try {
            var root = MAPPER.createObjectNode();
            root.put("v", VERSION);
            root.put("p", planFingerprint);
            var slots = root.putArray("s");
            for (Object value : values) {
                var item = slots.addObject();
                item.put("n", value == null);
                if (value != null) item.put("x", OrmCursor.format(value));
            }
            return Base64.getUrlEncoder().withoutPadding()
                    .encodeToString(MAPPER.writeValueAsBytes(root));
        } catch (com.fasterxml.jackson.core.JsonProcessingException e) {
            throw new IllegalStateException("Aggregate cursor could not be encoded.", e);
        }
    }

    /** Validates version, fingerprint, slot structure, and arity before returning typed values. */
    public static List<Object> decode(String cursor, String expectedFingerprint,
            List<Class<?>> valueTypes) {
        validateFingerprint(expectedFingerprint);
        Objects.requireNonNull(valueTypes, "valueTypes");
        if (cursor == null || cursor.isBlank())
            throw new IllegalArgumentException("Aggregate cursor is empty.");
        try {
            JsonNode root = MAPPER.readTree(Base64.getUrlDecoder().decode(cursor));
            if (root == null || !root.isObject() || root.size() != 3
                    || !root.path("v").isInt() || root.path("v").intValue() != VERSION
                    || !root.path("p").isTextual() || !root.path("s").isArray()) {
                throw new IllegalArgumentException("Aggregate cursor envelope is invalid.");
            }
            if (!constantTimeEquals(expectedFingerprint, root.path("p").asText()))
                throw new IllegalArgumentException("Aggregate cursor belongs to a different query plan.");
            var slots = new ArrayList<OrmAggregateCursor.Slot>();
            for (JsonNode item : root.path("s")) {
                if (!item.isObject() || !item.path("n").isBoolean()
                        || item.size() != (item.path("n").booleanValue() ? 1 : 2))
                    throw new IllegalArgumentException("Aggregate cursor slot is invalid.");
                boolean isNull = item.path("n").booleanValue();
                if (isNull) slots.add(new OrmAggregateCursor.Slot(true, null));
                else {
                    if (!item.path("x").isTextual())
                        throw new IllegalArgumentException("Aggregate cursor value is not textual.");
                    slots.add(new OrmAggregateCursor.Slot(false, item.path("x").asText()));
                }
            }
            var rawValues = new ArrayList<Object>(slots.size());
            for (var slot : slots) rawValues.add(slot.nullValue() ? null : slot.value());
            return OrmAggregateCursor.decodeTyped(OrmAggregateCursor.encode(rawValues), valueTypes);
        } catch (java.io.IOException | IllegalArgumentException | IllegalStateException e) {
            throw new IllegalArgumentException("The aggregate cursor is malformed or invalid for this plan.", e);
        }
    }

    private static void validateFingerprint(String fingerprint) {
        if (fingerprint == null || !fingerprint.matches("[0-9a-f]{64}"))
            throw new IllegalArgumentException("A lowercase SHA-256 query-plan fingerprint is required.");
    }

    private static boolean constantTimeEquals(String left, String right) {
        return MessageDigest.isEqual(left.getBytes(StandardCharsets.US_ASCII),
                right.getBytes(StandardCharsets.US_ASCII));
    }
}
