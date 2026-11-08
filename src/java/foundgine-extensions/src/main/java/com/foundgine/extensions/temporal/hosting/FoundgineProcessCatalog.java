package com.foundgine.extensions.temporal.hosting;

import com.foundgine.extensions.temporal.contracts.FoundgineProcessDefinition;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;

/** Server-side catalog of processes. Clients pick a name; only the host defines what runs. */
public final class FoundgineProcessCatalog {
    private final Map<String, FoundgineProcessDefinition> definitions = new LinkedHashMap<>();

    public synchronized FoundgineProcessCatalog add(FoundgineProcessDefinition definition) {
        Objects.requireNonNull(definition, "definition");
        if (definition.steps().isEmpty()) throw new IllegalArgumentException("A process needs at least one step.");
        long distinct = definition.steps().stream().map(s -> s.name()).distinct().count();
        if (distinct != definition.steps().size())
            throw new IllegalArgumentException("Step names must be unique within a process.");
        definitions.put(definition.name(), definition);
        return this;
    }

    public synchronized Optional<FoundgineProcessDefinition> tryGet(String name) {
        return Optional.ofNullable(name == null ? null : definitions.get(name));
    }

    public synchronized Collection<String> names() { return java.util.List.copyOf(definitions.keySet()); }
}
