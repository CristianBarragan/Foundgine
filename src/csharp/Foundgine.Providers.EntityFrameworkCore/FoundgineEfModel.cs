#pragma warning disable IL2026, IL3050

using System.Diagnostics.CodeAnalysis;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>
/// Extension surface of the EF provider. Everything is optional; the defaults are what a sample needs.
/// </summary>
public sealed class FoundgineEfOptions
{
    internal const DynamicallyAccessedMemberTypes EfEntityMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties |
        DynamicallyAccessedMemberTypes.Interfaces;

    private readonly List<IOrmEntityMap> _maps = [];
    private readonly List<Action<ModelBuilder>> _customizations = [];

    private readonly Dictionary<string, Type> _clrTypes = new(StringComparer.Ordinal);

    internal IReadOnlyList<IOrmEntityMap> Maps => _maps;
    internal IReadOnlyDictionary<string, Type> ClrTypes => _clrTypes;
    internal IReadOnlyList<Action<ModelBuilder>> Customizations => _customizations;
    internal bool RegisterEngineProvider { get; private set; }

    /// <summary>ORM capability profile for plan validation. Defaults to <see cref="OrmProviderCapabilities.SharedLinq"/>.</summary>
    public OrmProviderCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Replaces the reflective ORM map of an entity with a hand-written or generated one (the Native AOT path).
    /// The map's <c>EntityName</c> selects the entity it replaces.
    /// </summary>
    public FoundgineEfOptions AddMap(IOrmEntityMap map)
    {
        _maps.Add(map ?? throw new ArgumentNullException(nameof(map)));
        return this;
    }

    /// <summary>Several <see cref="AddMap"/> calls at once, e.g. <c>GeneratedOrmMaps.CreateAll()</c>.</summary>
    public FoundgineEfOptions AddMaps(IEnumerable<IOrmEntityMap> maps)
    {
        foreach (var map in maps ?? throw new ArgumentNullException(nameof(maps))) AddMap(map);
        return this;
    }

    /// <summary>
    /// Supplies CLR types by entity name for metadata that carries none (attribute/generator metadata).
    /// Fluent metadata already has them.
    /// </summary>
    public FoundgineEfOptions MapClrTypes(IEnumerable<KeyValuePair<string, Type>> types)
    {
        foreach (var (name, type) in types ?? throw new ArgumentNullException(nameof(types))) _clrTypes[name] = type;
        return this;
    }

    /// <summary>Adds EF configuration for one entity on top of the metadata-derived model (indexes, converters, ...).</summary>
    public FoundgineEfOptions ConfigureEntity<[DynamicallyAccessedMembers(EfEntityMembers)] T>(Action<EntityTypeBuilder<T>> configure) where T : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        _customizations.Add(b => configure(b.Entity<T>()));
        return this;
    }

    /// <summary>Full access to the EF <see cref="ModelBuilder"/>, applied after the metadata-derived model.</summary>
    public FoundgineEfOptions Customize(Action<ModelBuilder> configure)
    {
        _customizations.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
        return this;
    }

    /// <summary>
    /// Also registers <c>IProviderPlanCompiler</c> and a singleton <c>IExecutionProvider</c> (a DbContext per execution,
    /// from the factory) so the Foundgine engine pipeline (<c>AddFoundgine</c>) executes through EF Core.
    /// </summary>
    public FoundgineEfOptions UseEngineProvider()
    {
        RegisterEngineProvider = true;
        return this;
    }
}

/// <summary>
/// The compiled EF binding of one Foundgine model: the metadata, the ORM maps bound to it and the EF customizations.
/// Built once at startup so a model/ORM mismatch fails there, not on the first query.
/// </summary>
public sealed class FoundgineEfModel
{
    private readonly IReadOnlyList<Action<ModelBuilder>> _customizations;

    public FoundgineEfModel(MetadataRegistry metadata, FoundgineEfOptions? options = null)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        options ??= new FoundgineEfOptions();
        Options = options;
        _customizations = options.Customizations;

        // Entities with the CLR type resolved: from the metadata itself, else from MapClrTypes.
        Entities = metadata.Entities
            .Select(e => e.ClrType is null && options.ClrTypes.TryGetValue(e.Name, out var clr) ? e with { ClrType = clr } : e)
            .ToList();

        var overridden = options.Maps.Select(m => m.EntityName).ToHashSet(StringComparer.Ordinal);
        var maps = FluentOrmMaps.CreateAll(Entities.Where(e => !overridden.Contains(e.Name)))
            .Concat(options.Maps);
        Registry = OrmRegistry.Create(metadata.Entities, maps, metadata.Relationships);
        Capabilities = options.Capabilities ?? OrmProviderCapabilities.SharedLinq(FoundgineEfCore.ProviderName);
    }

    public MetadataRegistry Metadata { get; }

    /// <summary>The model's entities with their CLR types resolved (entities without one have no EF mapping).</summary>
    public IReadOnlyList<EntityMetadata> Entities { get; }

    public OrmRegistry Registry { get; }
    public OrmProviderCapabilities Capabilities { get; }
    internal FoundgineEfOptions Options { get; }

    /// <summary>Applies the metadata-derived EF model, then the registered customizations.</summary>
    public void Apply(ModelBuilder modelBuilder)
    {
        FluentEfModel.Apply(modelBuilder, Entities);
        foreach (var customize in _customizations) customize(modelBuilder);
    }
}

/// <summary>
/// The EF Core context of a Foundgine model. Its EF model is derived from the fluent metadata, so tables, keys and
/// columns have one source of truth. Open for extension: derive and override <see cref="OnFoundgineModelCreated"/>.
/// </summary>
public class FoundgineDbContext : DbContext
{
    private readonly FoundgineEfModel _model;

    public FoundgineDbContext(DbContextOptions<FoundgineDbContext> options, FoundgineEfModel model) : base(options) =>
        _model = model ?? throw new ArgumentNullException(nameof(model));

    protected FoundgineDbContext(DbContextOptions options, FoundgineEfModel model) : base(options) =>
        _model = model ?? throw new ArgumentNullException(nameof(model));

    public FoundgineEfModel FoundgineModel => _model;

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _model.Apply(modelBuilder);
        OnFoundgineModelCreated(modelBuilder);
    }

    /// <summary>Hook that runs after the metadata-derived model has been applied.</summary>
    protected virtual void OnFoundgineModelCreated(ModelBuilder modelBuilder)
    {
    }
}
