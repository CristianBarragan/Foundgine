using System.Linq.Expressions;

namespace Foundgine.Core.Semantic.Configuration;

/// <summary>
/// Maps a semantic model onto its persistence type, member by member. The mapping lives
/// outside both types so neither needs to reference the other.
/// </summary>
public abstract class FoundgineEntityMap<TModel, TEntity> : IFoundgineConfiguration
{
    public abstract void Configure(FoundgineEntityMapBuilder<TModel, TEntity> map);

    void IFoundgineConfiguration.Apply(FoundgineConfigurationContext context) =>
        Configure(new FoundgineEntityMapBuilder<TModel, TEntity>(context.AddMap(typeof(TModel), typeof(TEntity))));
}

public sealed class FoundgineEntityMapBuilder<TModel, TEntity>
{
    private readonly EntityMapDefinition _definition;

    internal FoundgineEntityMapBuilder(EntityMapDefinition definition) => _definition = definition;

    /// <summary>Selects a model member to map. Complete the mapping with <c>.To(x =&gt; x.Member)</c> on the storage type.</summary>
    public FoundgineEntityMapPropertyBuilder<TModel, TEntity, TProperty> Property<TProperty>(
        Expression<Func<TModel, TProperty>> modelProperty) =>
        new(_definition, ConfigurationExpression.Member(modelProperty));
}

public sealed class FoundgineEntityMapPropertyBuilder<TModel, TEntity, TProperty>
{
    private readonly EntityMapDefinition _definition;
    private readonly MemberRef _model;

    internal FoundgineEntityMapPropertyBuilder(EntityMapDefinition definition, MemberRef model)
    {
        _definition = definition;
        _model = model;
    }

    /// <summary>The storage member that holds this value. Both members must have the same CLR type.</summary>
    public FoundgineEntityMapBuilder<TModel, TEntity> To(Expression<Func<TEntity, TProperty>> entityProperty)
    {
        var entity = ConfigurationExpression.Member(entityProperty);
        if (_definition.Properties.Exists(p => p.Model.Name == _model.Name))
            throw new InvalidOperationException(
                $"{typeof(TModel).Name}.{_model.Name} is mapped more than once.");

        _definition.Properties.Add((_model, entity));
        return new FoundgineEntityMapBuilder<TModel, TEntity>(_definition);
    }
}
