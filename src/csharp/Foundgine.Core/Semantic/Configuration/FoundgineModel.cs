using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.Core.Semantic.Configuration;

/// <summary>
/// A single unit of Foundgine configuration: a semantic model, a persistence model,
/// an entity map or a connection. Implemented by the abstract
/// <c>Foundgine*Configuration</c> base classes; applications derive from those
/// rather than implementing this directly.
/// </summary>
public interface IFoundgineConfiguration
{
    void Apply(FoundgineConfigurationContext context);
}

/// <summary>Collects configuration state. Opaque to applications; created by <see cref="FoundgineModel"/>.</summary>
public sealed class FoundgineConfigurationContext
{
    internal FoundgineConfigurationContext()
    {
    }

    internal Dictionary<Type, ModelDefinition> Models { get; } = new();
    internal Dictionary<Type, PersistenceDefinition> Persistence { get; } = new();
    internal List<EntityMapDefinition> Maps { get; } = [];
    internal List<ConnectionDefinition> Connections { get; } = [];

    internal ModelDefinition AddModel(Type modelType)
    {
        var definition = new ModelDefinition(modelType);
        if (!Models.TryAdd(modelType, definition))
            throw new InvalidOperationException($"Semantic model '{modelType.Name}' is configured more than once.");
        return definition;
    }

    internal PersistenceDefinition AddPersistence(Type entityType)
    {
        var definition = new PersistenceDefinition(entityType);
        if (!Persistence.TryAdd(entityType, definition))
            throw new InvalidOperationException(
                $"Persistence model '{entityType.Name}' is configured more than once.");
        return definition;
    }

    internal EntityMapDefinition AddMap(Type modelType, Type entityType)
    {
        if (Maps.Exists(m => m.ModelType == modelType))
            throw new InvalidOperationException($"Model '{modelType.Name}' already has an entity map.");
        if (Maps.Exists(m => m.EntityType == entityType))
            throw new InvalidOperationException(
                $"Persistence model '{entityType.Name}' is already mapped from another semantic model.");

        var definition = new EntityMapDefinition(modelType, entityType);
        Maps.Add(definition);
        return definition;
    }

    internal ConnectionDefinition AddConnection(Type sourceModel, Type targetModel, string? navigation)
    {
        var definition = new ConnectionDefinition(sourceModel, targetModel, navigation);
        Connections.Add(definition);
        return definition;
    }
}

/// <summary>Registers configurations on a <see cref="FoundgineModel"/>.</summary>
public sealed class FoundgineModelBuilder
{
    private readonly FoundgineConfigurationContext _context;

    internal FoundgineModelBuilder(FoundgineConfigurationContext context) => _context = context;

    /// <summary>Applies a configuration class that has a public parameterless constructor.</summary>
    public FoundgineModelBuilder Configure<TConfiguration>()
        where TConfiguration : IFoundgineConfiguration, new() =>
        Configure(new TConfiguration());

    /// <summary>Applies an already constructed configuration (for example one that needs constructor arguments).</summary>
    public FoundgineModelBuilder Configure(IFoundgineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Apply(_context);
        return this;
    }
}

/// <summary>
/// The single registration point for an application's Foundgine model. Derive from it
/// and list every semantic, persistence, mapping and connection configuration.
/// <see cref="Build"/> validates the whole set and produces the same
/// <see cref="MetadataRegistry"/> that the attribute-based generator emits, so both
/// styles feed the same planner and providers.
/// </summary>
public abstract class FoundgineModel
{
    public abstract void Configure(FoundgineModelBuilder model);

    /// <summary>Validates the configuration and returns a new registry. Throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public MetadataRegistry Build()
    {
        var registry = new MetadataRegistry();
        FoundgineModelCompiler.Compile(Collect(), registry);
        return registry;
    }

    /// <summary>
    /// Validates the configuration and adds the result to an existing registry, for
    /// applications that mix fluent configuration with attribute-generated metadata.
    /// Throws if any entity, model or relationship id is already registered, so the
    /// two sources can never silently overwrite each other.
    /// </summary>
    public void BuildInto(MetadataRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var built = Build();
        var conflicts = new List<string>();

        foreach (var entity in built.Entities)
            if (registry.TryGet(entity.EntityId, out _))
                conflicts.Add($"entity '{entity.Name}'");
        foreach (var model in built.Models)
            if (registry.TryGet(model.Id, out _))
                conflicts.Add($"model '{model.Name}'");
        foreach (var relationship in built.Relationships)
            if (registry.TryGet(relationship.Id, out _))
                conflicts.Add($"relationship '{relationship.Name}'");

        if (conflicts.Count > 0)
            throw new InvalidOperationException(
                "Fluent Foundgine configuration conflicts with metadata already in the registry: " +
                string.Join(", ", conflicts) + ".");

        foreach (var entity in built.Entities) registry.Register(entity);
        foreach (var model in built.Models) registry.Register(model);
        foreach (var relationship in built.Relationships) registry.Register(relationship);
    }

    private FoundgineConfigurationContext Collect()
    {
        var context = new FoundgineConfigurationContext();
        Configure(new FoundgineModelBuilder(context));
        return context;
    }
}
