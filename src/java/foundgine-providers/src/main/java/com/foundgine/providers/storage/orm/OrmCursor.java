package com.foundgine.providers.storage.orm;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import java.math.BigDecimal;
import java.time.temporal.TemporalAccessor;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;
import java.util.Objects;

/**
 * Opaque cursor: base64url (no padding) of a JSON array of strings (null stays null). Cursors are
 * NOT signed: a client can forge a position, but only inside its own authorized result set,
 * because the keyset predicate is always AND-ed after authorization.
 */
public final class OrmCursor {
    private static final ObjectMapper MAPPER = new ObjectMapper();

    private OrmCursor() {}

    public static String encode(List<?> values) {
        Objects.requireNonNull(values, "values");
        try {
            var array = MAPPER.createArrayNode();
            for (var v : values) {
                if (v == null) array.addNull();
                else array.add(format(v));
            }
            return Base64.getUrlEncoder()
                    .withoutPadding()
                    .encodeToString(MAPPER.writeValueAsBytes(array));
        } catch (com.fasterxml.jackson.core.JsonProcessingException e) {
            throw new IllegalStateException("Cursor could not be encoded.", e);
        }
    }

    public static List<String> decode(String cursor) {
        if (cursor == null || cursor.isBlank()) {
            throw new IllegalStateException("The pagination cursor is empty.");
        }
        try {
            JsonNode root = MAPPER.readTree(Base64.getUrlDecoder().decode(cursor));
            if (root == null || !root.isArray()) {
                throw new IllegalArgumentException("Cursor payload is not an array.");
            }
            var result = new ArrayList<String>();
            for (JsonNode item : root) {
                if (item.isNull()) result.add(null);
                else if (item.isTextual()) result.add(item.asText());
                else throw new IllegalArgumentException("Cursor payload contains a non-string.");
            }
            return result;
        } catch (java.io.IOException | IllegalArgumentException e) {
            throw new IllegalStateException("The pagination cursor is malformed.", e);
        }
    }

    static String format(Object value) {
        if (value instanceof BigDecimal d) return d.toPlainString();
        if (value instanceof TemporalAccessor || value instanceof Number) return value.toString();
        if (value instanceof Enum<?> e) return Integer.toString(e.ordinal());
        return value.toString();
    }
}
