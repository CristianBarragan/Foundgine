package com.foundgine.core.semantic.configuration;

import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.ModelMetadata;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * The single registration point for an application's Foundgine model. Extend it and list every
 * semantic, persistence, mapping and connection configuration. {@link #build()} validates the
 * whole set and produces the same {@link MetadataRegistry} that attribute/annotation-based
 * metadata produces, so both styles feed the same planner and providers.
 */
public abstract class FoundgineModel {
    public abstract void configure(FoundgineModelRegistrar model);

    /** Validates the configuration and returns a new registry; throws one exception listing every problem found. */
    public MetadataRegistry build() {
        MetadataRegistry registry = new MetadataRegistry();
        FoundgineModelCompiler.compile(collect(), registry);
        return registry;
    }

    /**
     * Validates the configuration and adds the result to an existing registry, for applications
     * that mix fluent configuration with annotation-generated metadata. Throws if any entity,
     * model or relationship id is already registered, so the two sources never silently
     * overwrite each other.
     */
    public void buildInto(MetadataRegistry registry) {
        Objects.requireNonNull(registry, "registry");

        MetadataRegistry built = build();
        List<String> conflicts = new ArrayList<>();
        for (EntityMetadata entity : built.entities())
            if (registry.tryGet(entity.entityId())) conflicts.add("entity '" + entity.name() + "'");
        for (ModelMetadata model : built.models())
            if (registry.tryGet(model.id())) conflicts.add("model '" + model.name() + "'");
        for (RelationshipMetadata relationship : built.relationships())
            if (registry.tryGet(relationship.id()))
                conflicts.add("relationship '" + relationship.name() + "'");

        if (!conflicts.isEmpty())
            throw new IllegalStateException(
                    "Fluent Foundgine configuration conflicts with metadata already in the registry: "
                            + String.join(", ", conflicts)
                            + ".");

        for (EntityMetadata entity : built.entities()) registry.register(entity);
        for (ModelMetadata model : built.models()) registry.register(model);
        for (RelationshipMetadata relationship : built.relationships()) registry.register(relationship);
    }

    private FoundgineConfigurationContext collect() {
        FoundgineConfigurationContext context = new FoundgineConfigurationContext();
        configure(new FoundgineModelRegistrar(context));
        return context;
    }
}
