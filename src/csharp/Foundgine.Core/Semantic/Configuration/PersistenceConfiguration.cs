using System.Linq.Expressions;

namespace Foundgine.Core.Semantic.Configuration;

/// <summary>
/// Provider-independent persistence configuration for one storage type: table, columns,
/// key and indexes. Execution integrations (SQL, EF Core, ...) translate this metadata;
/// it carries no dependency on any of them.
/// </summary>
public abstract class FoundginePersistenceConfiguration<TEntity> : IFoundgineConfiguration
{
    public abstract void Configure(FoundginePersistenceBuilder<TEntity> model);

    void IFoundgineConfiguration.Apply(FoundgineConfigurationContext context) =>
        Configure(new FoundginePersistenceBuilder<TEntity>(context.AddPersistence(typeof(TEntity))));
}

public sealed class FoundginePersistenceBuilder<TEntity>
{
    private readonly PersistenceDefinition _definition;

    internal FoundginePersistenceBuilder(PersistenceDefinition definition) => _definition = definition;

    /// <summary>Sets the physical table and optional schema. Defaults to the storage type name when omitted.</summary>
    public FoundginePersistenceBuilder<TEntity> ToTable(string name, string? schema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (schema is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        _definition.Table = name.Trim();
        _definition.Schema = schema?.Trim();
        return this;
    }

    /// <summary>Declares the primary key. Composite keys are not supported by Foundgine metadata.</summary>
    public FoundginePersistenceBuilder<TEntity> HasKey(Expression<Func<TEntity, object?>> key)
    {
        var members = ConfigurationExpression.Members(key);
        if (members.Count != 1)
            throw new NotSupportedException(
                $"{typeof(TEntity).Name}: composite primary keys are not supported; Foundgine entities have a single key column.");

        _definition.Key = members[0];
        _definition.Column(members[0]);
        return this;
    }

    public FoundginePropertyBuilder Property<TProperty>(Expression<Func<TEntity, TProperty>> property) =>
        new(_definition.Column(ConfigurationExpression.Member(property)));

    /// <summary>
    /// Declares an index. Foundgine records this as an access-path hint on the leading
    /// column; it does not create the index and does not model uniqueness.
    /// </summary>
    public FoundginePersistenceBuilder<TEntity> HasIndex(Expression<Func<TEntity, object?>> members)
    {
        var list = ConfigurationExpression.Members(members);
        foreach (var member in list)
            _definition.Column(member);

        _definition.Indexes.Add(list);
        return this;
    }
}

public sealed class FoundginePropertyBuilder
{
    private readonly ColumnDefinition _column;

    internal FoundginePropertyBuilder(ColumnDefinition column) => _column = column;

    public FoundginePropertyBuilder HasColumnName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _column.ColumnName = name.Trim();
        return this;
    }

    public FoundginePropertyBuilder HasMaxLength(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        _column.MaxLength = length;
        return this;
    }

    public FoundginePropertyBuilder IsRequired(bool required = true)
    {
        _column.IsRequired = required;
        return this;
    }
}
