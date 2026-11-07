using System.Linq.Expressions;

namespace Foundgine.Core.Semantic.Configuration;

/// <summary>
/// Connects semantic relationships (declared in model configurations) to the physical
/// foreign key that realises them. One connection configures both directions of the pair.
/// </summary>
public abstract class FoundgineConnectionConfiguration : IFoundgineConfiguration
{
    public abstract void Configure(FoundgineConnectionBuilder connections);

    void IFoundgineConfiguration.Apply(FoundgineConfigurationContext context) =>
        Configure(new FoundgineConnectionBuilder(context));
}

public sealed class FoundgineConnectionBuilder
{
    private readonly FoundgineConfigurationContext _context;

    internal FoundgineConnectionBuilder(FoundgineConfigurationContext context) => _context = context;

    /// <summary>
    /// Configures the relationship between two semantic models. When the same two models
    /// are related more than once (for example billing and shipping customer), pass the
    /// navigation on <typeparamref name="TSource"/> that identifies which one this is.
    /// </summary>
    public FoundgineRelationshipConnectionBuilder<TSource, TTarget> HasRelationship<TSource, TTarget>(
        Expression<Func<TSource, object?>>? navigation = null)
    {
        var name = navigation is null ? null : ConfigurationExpression.Member(navigation).Name;
        return new FoundgineRelationshipConnectionBuilder<TSource, TTarget>(
            _context.AddConnection(typeof(TSource), typeof(TTarget), name));
    }
}

public sealed class FoundgineRelationshipConnectionBuilder<TSource, TTarget>
{
    private readonly ConnectionDefinition _definition;

    internal FoundgineRelationshipConnectionBuilder(ConnectionDefinition definition) => _definition = definition;

    /// <summary>The storage types backing the source and target models.</summary>
    public FoundgineStorageConnectionBuilder<TSourceEntity, TTargetEntity> Using<TSourceEntity, TTargetEntity>()
    {
        _definition.SourceEntity = typeof(TSourceEntity);
        _definition.TargetEntity = typeof(TTargetEntity);
        return new FoundgineStorageConnectionBuilder<TSourceEntity, TTargetEntity>(_definition);
    }
}

public sealed class FoundgineStorageConnectionBuilder<TSourceEntity, TTargetEntity>
{
    private readonly ConnectionDefinition _definition;

    internal FoundgineStorageConnectionBuilder(ConnectionDefinition definition) => _definition = definition;

    /// <summary>The foreign key lives on the target storage type (the usual one-to-many shape: Customer 1 - * Order).</summary>
    public FoundgineReferenceBuilder<TSourceEntity> HasForeignKey(Expression<Func<TTargetEntity, object?>> foreignKey)
    {
        _definition.ForeignKeyOnTarget = true;
        _definition.ForeignKey = Single(foreignKey);
        return new FoundgineReferenceBuilder<TSourceEntity>(_definition);
    }

    /// <summary>The foreign key lives on the source storage type (for example Order.CustomerId when the source is Order).</summary>
    public FoundgineReferenceBuilder<TTargetEntity> HasForeignKeyOnSource(
        Expression<Func<TSourceEntity, object?>> foreignKey)
    {
        _definition.ForeignKeyOnTarget = false;
        _definition.ForeignKey = Single(foreignKey);
        return new FoundgineReferenceBuilder<TTargetEntity>(_definition);
    }

    private static MemberRef Single<TEntity>(Expression<Func<TEntity, object?>> key)
    {
        var members = ConfigurationExpression.Members(key);
        return members.Count == 1
            ? members[0]
            : throw new NotSupportedException("Composite foreign keys are not supported.");
    }
}

public sealed class FoundgineReferenceBuilder<TPrincipal>
{
    private readonly ConnectionDefinition _definition;

    internal FoundgineReferenceBuilder(ConnectionDefinition definition) => _definition = definition;

    /// <summary>The key on the principal storage type that the foreign key points at.</summary>
    public void References(Expression<Func<TPrincipal, object?>> principalKey)
    {
        var members = ConfigurationExpression.Members(principalKey);
        if (members.Count != 1)
            throw new NotSupportedException("Composite principal keys are not supported.");

        _definition.References = members[0];
    }
}
