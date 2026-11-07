using System.Linq.Expressions;
using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.Core.Semantic.Configuration;

/// <summary>
/// Application/semantic configuration for one model: its semantic entity name, aliases,
/// dimensions and relationships. Says nothing about tables, columns or providers.
/// </summary>
public abstract class FoundgineModelConfiguration<TModel> : IFoundgineConfiguration
{
    public abstract void Configure(FoundgineModelBuilder<TModel> model);

    void IFoundgineConfiguration.Apply(FoundgineConfigurationContext context) =>
        Configure(new FoundgineModelBuilder<TModel>(context.AddModel(typeof(TModel))));
}

public sealed class FoundgineModelBuilder<TModel>
{
    private readonly ModelDefinition _definition;

    internal FoundgineModelBuilder(ModelDefinition definition) => _definition = definition;

    /// <summary>Sets the semantic entity name. Defaults to the model type name when omitted.</summary>
    public FoundgineModelBuilder<TModel> ToEntity(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _definition.EntityName = name.Trim();
        return this;
    }

    /// <summary>Declares a historical or synonymous name for the entity, with an optional evidence weight (1-100).</summary>
    public FoundgineModelBuilder<TModel> HasAlias(string name, int? weight = null)
    {
        _definition.Aliases.Add(ConfigurationAlias.Create(name, weight));
        return this;
    }

    /// <summary>Minimum alias-weight requirement (1-100) for this model's entity; see AliasWeightEvidenceGate.</summary>
    public FoundgineModelBuilder<TModel> HasMinimumWeight(int weight)
    {
        if (weight is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "Weight must be between 1 and 100 (inclusive).");
        _definition.MinimumWeight = weight;
        return this;
    }

    /// <summary>Marks the entity as an event (an occurrence in time) rather than current state, optionally naming the timestamp member.</summary>
    public FoundgineModelBuilder<TModel> IsEvent(Expression<Func<TModel, object?>>? occurredAt = null)
    {
        _definition.IsEvent = true;
        _definition.OccurredAtMember = occurredAt is null ? null : ConfigurationExpression.Member(occurredAt).Name;
        return this;
    }

    /// <summary>Marks a member as a semantic dimension. The dimension name defaults to the member name; use <c>.As(...)</c> to name it.</summary>
    public FoundgineDimensionBuilder HasDimension<TProperty>(Expression<Func<TModel, TProperty>> property)
    {
        var member = ConfigurationExpression.Member(property);
        _definition.Dimensions[member.Name] = member.Name;
        return new FoundgineDimensionBuilder(_definition, member.Name);
    }

    /// <summary>Configures semantic details of a single member, such as field aliases.</summary>
    public FoundgineFieldBuilder Property<TProperty>(Expression<Func<TModel, TProperty>> property)
    {
        var member = ConfigurationExpression.Member(property);
        return new FoundgineFieldBuilder(_definition, member.Name);
    }

    /// <summary>Declares a semantic relationship through a navigation member. Continue with <c>.To&lt;TTarget&gt;()</c>.</summary>
    public FoundgineRelationshipBuilder<TModel> HasRelationship(Expression<Func<TModel, object?>> navigation) =>
        new(_definition, ConfigurationExpression.Member(navigation));

}

public sealed class FoundgineDimensionBuilder
{
    private readonly ModelDefinition _definition;
    private readonly string _member;

    internal FoundgineDimensionBuilder(ModelDefinition definition, string member)
    {
        _definition = definition;
        _member = member;
    }

    /// <summary>Names the dimension this member represents, for example "tenant" or "country".</summary>
    public FoundgineDimensionBuilder As(string dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimension);
        _definition.Dimensions[_member] = dimension.Trim();
        return this;
    }
}

public sealed class FoundgineFieldBuilder
{
    private readonly ModelDefinition _definition;
    private readonly string _member;

    internal FoundgineFieldBuilder(ModelDefinition definition, string member)
    {
        _definition = definition;
        _member = member;
    }

    public FoundgineFieldBuilder HasAlias(string name, int? weight = null)
    {
        if (!_definition.FieldAliases.TryGetValue(_member, out var aliases))
            _definition.FieldAliases[_member] = aliases = [];

        aliases.Add(ConfigurationAlias.Create(name, weight));
        return this;
    }
}

public sealed class FoundgineRelationshipBuilder<TModel>
{
    private readonly ModelDefinition _definition;
    private readonly MemberRef _navigation;

    internal FoundgineRelationshipBuilder(ModelDefinition definition, MemberRef navigation)
    {
        _definition = definition;
        _navigation = navigation;
    }

    /// <summary>The semantic model on the other end of the relationship.</summary>
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> To<TTarget>()
    {
        var element = ConfigurationExpression.IsCollection(_navigation.Type)
            ? ConfigurationExpression.ElementType(_navigation.Type)
            : _navigation.Type;

        if (element is not null && !typeof(TTarget).IsAssignableFrom(element))
            throw new ArgumentException(
                $"{typeof(TModel).Name}.{_navigation.Name} is a {element.Name} navigation and cannot target {typeof(TTarget).Name}.");

        if (_definition.Relationships.Exists(r => r.Member == _navigation.Name))
            throw new InvalidOperationException(
                $"Relationship {typeof(TModel).Name}.{_navigation.Name} is declared more than once.");

        var relationship = new RelationshipDefinition(_navigation.Name, _navigation.Type, typeof(TTarget));
        _definition.Relationships.Add(relationship);
        return new FoundgineRelationshipTargetBuilder<TModel, TTarget>(relationship);
    }
}

public sealed class FoundgineRelationshipTargetBuilder<TModel, TTarget>
{
    private readonly RelationshipDefinition _relationship;

    internal FoundgineRelationshipTargetBuilder(RelationshipDefinition relationship) => _relationship = relationship;

    /// <summary>The inverse navigation on the target is a collection, e.g. <c>.WithMany(x =&gt; x.Orders)</c>.</summary>
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> WithMany(Expression<Func<TTarget, object?>> inverse)
    {
        var member = ConfigurationExpression.Member(inverse);
        if (!ConfigurationExpression.IsCollection(member.Type))
            throw new ArgumentException(
                $"{typeof(TTarget).Name}.{member.Name} is not a collection; use WithOne for a single-valued inverse.");

        _relationship.InverseMember = member.Name;
        _relationship.InverseIsCollection = true;
        return this;
    }

    /// <summary>The inverse navigation on the target is a single reference.</summary>
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> WithOne(Expression<Func<TTarget, object?>> inverse)
    {
        var member = ConfigurationExpression.Member(inverse);
        if (ConfigurationExpression.IsCollection(member.Type))
            throw new ArgumentException(
                $"{typeof(TTarget).Name}.{member.Name} is a collection; use WithMany for a collection inverse.");

        _relationship.InverseMember = member.Name;
        _relationship.InverseIsCollection = false;
        return this;
    }

    /// <summary>Sets the semantic name of the relationship (defaults to the navigation member name).</summary>
    public FoundgineRelationshipTargetBuilder<TModel, TTarget> Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _relationship.Name = name;
        return this;
    }

    public FoundgineRelationshipTargetBuilder<TModel, TTarget> HasAlias(string name, int? weight = null)
    {
        _relationship.Aliases.Add(ConfigurationAlias.Create(name, weight));
        return this;
    }
}
