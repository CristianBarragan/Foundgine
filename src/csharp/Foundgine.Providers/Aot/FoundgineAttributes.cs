namespace Foundgine.Providers.Aot;

/// <summary>Marks a storage/entity type for Foundgine compile-time metadata generation.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FoundgineEntityAttribute : Attribute
{
    public FoundgineEntityAttribute(string? name = null) => Name = name;
    public string? Name { get; init; }
    public string? StorageName { get; init; }
    public ulong Id { get; init; }
}

/// <summary>Declares one or more historical/synonymous semantic names for an AOT entity,
/// field, or relationship. Aliases resolve to the canonical declaration and never
/// participate in identity generation. Multiple names can be declared in a single
/// application - <c>[FoundgineAlias("aa", "bb")]</c> or <c>[FoundgineAlias(["aa", "bb"])]</c> -
/// and/or by stacking the attribute multiple times; both forms are merged.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class FoundgineAliasAttribute : Attribute
{
    public FoundgineAliasAttribute(params string[] names) => Names = names;

    /// <summary>The declared alias names. Contains a single entry for the common
    /// <c>[FoundgineAlias("name")]</c> form.</summary>
    public string[] Names { get; }

    private int _weight;

    /// <summary>Optional evidence weight from 1 to 100 (inclusive),
    /// applied to every name declared on this attribute instance. Stack the
    /// attribute once per name to give different names different weights.
    /// Leave unset (0) to declare no weight.</summary>
    public int Weight
    {
        get => _weight;
        init
        {
            if (value is < 1 or > 100)
                throw new ArgumentOutOfRangeException(
                    nameof(Weight),
                    value,
                    "Weight must be between 1 and 100 (inclusive) when specified.");
            _weight = value;
        }
    }
}

/// <summary>Marks an application model for compile-time semantic metadata generation.
/// Models are never instantiated, populated, or used as ORM entities by Foundgine.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FoundgineModelAttribute : Attribute
{
    public FoundgineModelAttribute(string? name = null) => Name = name;
    public string? Name { get; }
    public ulong Id { get; init; }

    private int _minimumWeight;

    /// <summary>Optional minimum alias-weight requirement from 1 to 100
    /// (inclusive) for this model's mapped entities. When set, each mapped
    /// entity's own average alias weight must meet this minimum, and the
    /// average across entities must also meet it, or the weighted alias
    /// evidence is treated as inconclusive (see
    /// <see cref="Foundgine.Core.Semantic.AliasWeightEvidenceGate"/>). Leave
    /// unset (0) to declare no minimum.</summary>
    public int MinimumWeight
    {
        get => _minimumWeight;
        init
        {
            if (value is < 1 or > 100)
                throw new ArgumentOutOfRangeException(
                    nameof(MinimumWeight),
                    value,
                    "MinimumWeight must be between 1 and 100 (inclusive) when specified.");
            _minimumWeight = value;
        }
    }
}

/// <summary>Overrides generated field metadata for a scalar entity property.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FoundgineFieldAttribute : Attribute
{
    public FoundgineFieldAttribute(string? name = null) => Name = name;
    public string? Name { get; init; }
    public string? StorageName { get; init; }
    public ulong Id { get; init; }

    /// <summary>Optional explicit physical column identity. When omitted, the column identity is derived from storage name and physical column name.</summary>
    public ulong ColumnId { get; init; }

    public bool IsPrimaryKey { get; init; }

    /// <summary>Hints that this field is (or should be) backed by a storage index.
    /// Providers may use this to prioritize indexed access paths during query
    /// planning; it does not by itself create an index.</summary>
    public bool Index { get; init; }
}

/// <summary>Marks a scalar field as a semantic dimension: an axis a query planner
/// can use for filtering, authorization, aggregation, or traversal (e.g. a tenant,
/// country, category, or business-unit key) rather than a plain data value.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FoundgineSemanticDimensionAttribute : Attribute
{
    public FoundgineSemanticDimensionAttribute(string dimension) => Dimension = dimension;

    /// <summary>The dimension name, e.g. "tenant", "country", "category".</summary>
    public string Dimension { get; }
}

/// <summary>Marks an entity as representing an occurrence at a point in time
/// (an event) rather than the current state of something. Event entities are
/// immutable once recorded and are the natural subject of temporal/"as of"
/// queries, time-series aggregation, and forecasting - as opposed to state
/// entities, which describe the current condition of a thing and can change
/// in place.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FoundgineEventAttribute : Attribute
{
    /// <param name="occurredAtField">Optional name of the scalar property that
    /// carries the timestamp the event occurred at. When omitted, the entity is
    /// still treated as an event, just without a declared temporal column.</param>
    public FoundgineEventAttribute(string? occurredAtField = null) => OccurredAtField = occurredAtField;

    public string? OccurredAtField { get; }
}

/// <summary>Declares a storage relationship. EF remains the authoritative
/// source for relational schema and foreign-key configuration.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FoundgineRelationshipAttribute : Attribute
{
    public FoundgineRelationshipAttribute(Type target, string foreignKey, string principalKey)
    {
        Target = target;
        ForeignKey = foreignKey;
        PrincipalKey = principalKey;
    }

    public Type Target { get; }
    public string ForeignKey { get; }
    public string PrincipalKey { get; }
    public ulong Id { get; init; }
    public string? Name { get; init; }
}

/// <summary>Declares a semantic model connection. It describes a visit from a
/// model to a known entity; it does not describe object construction or runtime
/// object mapping. A connection may optionally be represented by a plain LINQ
/// expression projection whose values are analyzed at compile time.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FoundgineConnectionAttribute : Attribute
{
    /// <summary>Creates a semantic connection without coupling the model to a storage/entity type.
    /// The target is supplied by <see cref="FoundgineConnectionMapAttribute"/> in the schema/infrastructure layer.</summary>
    public FoundgineConnectionAttribute()
    {
    }

    /// <summary>Legacy overload retained for compatibility. New code should use the parameterless
    /// form and an explicit <see cref="FoundgineConnectionMapAttribute"/>.</summary>
    public FoundgineConnectionAttribute(Type target) => Target = target;

    public Type? Target { get; }
    public ulong Id { get; init; }
    public string? Name { get; init; }
}

/// <summary>Explicitly maps a semantic model connection to a storage/entity type.
/// This declaration belongs in the schema/infrastructure boundary so the model and
/// storage entity do not need to reference one another.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FoundgineConnectionMapAttribute : Attribute
{
    public FoundgineConnectionMapAttribute(Type model, string connectionMember, Type entity)
    {
        Model = model;
        ConnectionMember = connectionMember;
        Entity = entity;
    }

    public Type Model { get; }
    public string ConnectionMember { get; }
    public Type Entity { get; }
}

/// <summary>Explicitly maps a semantic model to its persistence/entity representation.
/// The mapping is kept outside both types so neither side depends on the other.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FoundgineModelEntityMapAttribute : Attribute
{
    public FoundgineModelEntityMapAttribute(Type model, Type entity)
    {
        Model = model;
        Entity = entity;
    }

    public Type Model { get; }
    public Type Entity { get; }
}

/// <summary>Declares an AOT authorization predicate for a semantic connection.
/// The property should expose a plain LINQ expression such as
/// <c>Expression&lt;Func&lt;UserContext, Account, bool&gt;&gt;</c>. Foundgine analyzes
/// the expression at build time; it does not invoke it to populate objects.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FoundgineAuthorizationAttribute : Attribute
{
    public FoundgineAuthorizationAttribute(ulong connectionId) => ConnectionId = connectionId;
    public ulong ConnectionId { get; }
    public ulong Id { get; init; }
    public string? Name { get; init; }
}