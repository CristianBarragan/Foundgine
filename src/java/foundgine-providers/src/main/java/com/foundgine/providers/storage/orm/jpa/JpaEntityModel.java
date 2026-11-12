package com.foundgine.providers.storage.orm.jpa;

import java.util.LinkedHashSet;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.function.BiConsumer;
import java.util.function.Function;
import java.util.function.Supplier;

/**
 * Compile-time supplied access plan for one JPA entity. Generated models use direct Java lambdas;
 * no member lookup, accessibility override, reflective constructor, or dynamic class generation is needed.
 *
 * <p>Attribute keys are Java/JPA attribute names and must match {@code OrmFieldBinding.name()}.
 */
public final class JpaEntityModel<T> {
    private final Class<T> type;
    private final Supplier<T> factory;
    private final Map<String, Function<T, Object>> readers;
    private final Map<String, BiConsumer<T, Object>> writers;
    private final Set<String> attributes;
    private final String idAttribute;

    public JpaEntityModel(Class<T> type, Supplier<T> factory,
            Map<String, Function<T, Object>> readers,
            Map<String, BiConsumer<T, Object>> writers) {
        this(type, factory, readers, writers, null, null);
    }

    /**
     * Full generated form. {@code attributes} is every persistent attribute the class declares (not only those
     * with accessors), so startup validation needs no reflection; {@code idAttribute} names the single
     * identifier attribute, or is null when unknown or composite. A null {@code attributes} defaults to the
     * union of reader and writer names.
     */
    public JpaEntityModel(Class<T> type, Supplier<T> factory,
            Map<String, Function<T, Object>> readers,
            Map<String, BiConsumer<T, Object>> writers,
            Set<String> attributes, String idAttribute) {
        this.type = Objects.requireNonNull(type, "type");
        this.factory = Objects.requireNonNull(factory, "factory");
        this.readers = Map.copyOf(Objects.requireNonNull(readers, "readers"));
        this.writers = Map.copyOf(Objects.requireNonNull(writers, "writers"));
        var all = new LinkedHashSet<String>();
        if (attributes == null) { all.addAll(this.readers.keySet()); all.addAll(this.writers.keySet()); }
        else all.addAll(attributes);
        this.attributes = Set.copyOf(all);
        this.idAttribute = idAttribute;
    }

    /** Every persistent attribute the entity class declares. */
    public Set<String> attributes() { return attributes; }

    /** Single identifier attribute name, or null when unknown or composite. */
    public String idAttribute() { return idAttribute; }

    public boolean hasAttribute(String attribute) { return attributes.contains(attribute); }

    public boolean canRead(String attribute) { return readers.containsKey(attribute); }

    public Class<T> type() { return type; }

    public T create() {
        return Objects.requireNonNull(factory.get(), "JPA entity factory returned null");
    }

    public Object read(T entity, String attribute) {
        var reader = readers.get(attribute);
        if (reader == null) throw new IllegalStateException("No generated reader for '" + attribute + "'.");
        return reader.apply(entity);
    }

    public void write(T entity, String attribute, Object value) {
        var writer = writers.get(attribute);
        if (writer == null) throw new IllegalStateException("No generated writer for '" + attribute + "'.");
        writer.accept(entity, value);
    }

    public boolean canWrite(String attribute) { return writers.containsKey(attribute); }
}
