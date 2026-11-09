package com.foundgine.extensions.graphql;

import java.util.AbstractMap;
import java.util.AbstractSet;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

/**
 * Insertion-ordered map with case-insensitive string keys. This is the Java counterpart of the C#
 * {@code ToDictionary(..., StringComparer.OrdinalIgnoreCase)} objects used for GraphQL input values: enumeration order is
 * the order the keys were written, which matters for {@code order: { a: ASC, b: DESC }} (the order terms are applied in
 * that sequence). A {@code TreeMap} with {@code CASE_INSENSITIVE_ORDER} would silently re-sort the keys.
 * Read-only after construction by {@link #freeze()}.
 */
final class CaseInsensitiveOrderedMap extends AbstractMap<String, Object> {
    private final LinkedHashMap<String, Map.Entry<String, Object>> entries = new LinkedHashMap<>();
    private boolean frozen;

    private static String fold(Object key) {
        return String.valueOf(key).toLowerCase(Locale.ROOT);
    }

    CaseInsensitiveOrderedMap freeze() {
        frozen = true;
        return this;
    }

    @Override
    public Object put(String key, Object value) {
        if (frozen) throw new UnsupportedOperationException("GraphQL input objects are read-only.");
        var folded = fold(key);
        var existing = entries.get(folded);
        if (existing != null) {
            var old = existing.getValue();
            entries.put(folded, new SimpleEntry<>(existing.getKey(), value));
            return old;
        }
        entries.put(folded, new SimpleEntry<>(key, value));
        return null;
    }

    @Override
    public boolean containsKey(Object key) {
        return key instanceof String && entries.containsKey(fold(key));
    }

    @Override
    public Object get(Object key) {
        if (!(key instanceof String)) return null;
        var entry = entries.get(fold(key));
        return entry == null ? null : entry.getValue();
    }

    @Override
    public int size() {
        return entries.size();
    }

    @Override
    public Set<Entry<String, Object>> entrySet() {
        return new AbstractSet<>() {
            @Override
            public Iterator<Entry<String, Object>> iterator() {
                var inner = entries.values().iterator();
                return new Iterator<>() {
                    @Override
                    public boolean hasNext() {
                        return inner.hasNext();
                    }

                    @Override
                    public Entry<String, Object> next() {
                        var e = inner.next();
                        return new SimpleImmutableEntry<>(e.getKey(), e.getValue());
                    }
                };
            }

            @Override
            public int size() {
                return entries.size();
            }
        };
    }
}
