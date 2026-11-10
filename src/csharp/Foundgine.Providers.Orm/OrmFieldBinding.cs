using System.Linq.Expressions;

namespace Foundgine.Providers.Orm;

/// <summary>
/// Compile-time generated, strongly typed operations for one entity field. Every member is a static lambda
/// emitted by the source generator, so the runtime never discovers properties by reflection.
/// </summary>
public sealed class OrmFieldBinding<TEntity> where TEntity : class
{
    public OrmFieldBinding(
        string name,
        Type clrType,
        bool isPrimaryKey,
        Func<TEntity, object?> get,
        Expression<Func<TEntity, object?>> selector,
        Func<object?, Expression<Func<TEntity, bool>>> equal,
        Func<object?, Expression<Func<TEntity, bool>>> notEqual,
        Func<IReadOnlyList<object?>, Expression<Func<TEntity, bool>>> isIn,
        Func<IQueryable<TEntity>, bool, IOrderedQueryable<TEntity>> orderBy,
        Func<IOrderedQueryable<TEntity>, bool, IOrderedQueryable<TEntity>> thenBy,
        Func<object?, Expression<Func<TEntity, bool>>>? greaterThan = null,
        Func<object?, Expression<Func<TEntity, bool>>>? lessThan = null,
        Action<TEntity, object?>? set = null)
    {
        Name = name;
        ClrType = clrType;
        IsPrimaryKey = isPrimaryKey;
        Get = get;
        Selector = selector;
        Equal = equal;
        NotEqual = notEqual;
        In = isIn;
        OrderBy = orderBy;
        ThenBy = thenBy;
        GreaterThan = greaterThan;
        LessThan = lessThan;
        Set = set;
    }

    public string Name { get; }
    public Type ClrType { get; }
    public bool IsPrimaryKey { get; }
    public Func<TEntity, object?> Get { get; }

    /// <summary>
    /// A statically generated field selector. The shared executor composes these selectors into a provider
    /// projection without compiling delegates or materializing complete entities.
    /// </summary>
    public Expression<Func<TEntity, object?>> Selector { get; }

    public Func<object?, Expression<Func<TEntity, bool>>> Equal { get; }
    public Func<object?, Expression<Func<TEntity, bool>>> NotEqual { get; }
    public Func<IReadOnlyList<object?>, Expression<Func<TEntity, bool>>> In { get; }
    public Func<IQueryable<TEntity>, bool, IOrderedQueryable<TEntity>> OrderBy { get; }
    public Func<IOrderedQueryable<TEntity>, bool, IOrderedQueryable<TEntity>> ThenBy { get; }

    /// <summary>Keyset comparison (field &gt; value). Null when the field type has no translatable ordering
    /// operator (for example Guid or bool): cursor paging over such a field fails closed.</summary>
    public Func<object?, Expression<Func<TEntity, bool>>>? GreaterThan { get; }

    /// <summary>Keyset comparison (field &lt; value); null under the same conditions as <see cref="GreaterThan"/>.</summary>
    public Func<object?, Expression<Func<TEntity, bool>>>? LessThan { get; }

    /// <summary>Typed setter used by mutations; null for read-only (get-only / init-only) properties.</summary>
    public Action<TEntity, object?>? Set { get; }
}
