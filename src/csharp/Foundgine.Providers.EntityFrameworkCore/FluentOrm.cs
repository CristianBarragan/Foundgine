
#pragma warning disable IL2026, IL2055, IL2060, IL2065, IL2070, IL2072, IL2075, IL2087, IL3050

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Orm;
using Microsoft.EntityFrameworkCore;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>
/// Applies fluent Foundgine metadata to an EF Core model: table, key and column names come from the metadata,
/// and members that are not mapped fields (navigation members) are ignored.
/// </summary>
public static class FluentEfModel
{
    [RequiresUnreferencedCode("Reads entity members by reflection; not trim/AOT safe.")]
    public static void Apply(ModelBuilder modelBuilder, MetadataRegistry metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Apply(modelBuilder, metadata.Entities);
    }

    /// <summary>Applies entities whose CLR types are already resolved.</summary>
    [RequiresUnreferencedCode("Reads entity members by reflection; not trim/AOT safe.")]
    public static void Apply(ModelBuilder modelBuilder, IEnumerable<EntityMetadata> entities)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(entities);

        foreach (var entity in entities)
        {
            if (entity.ClrType is not { } clr) continue;
            var builder = modelBuilder.Entity(clr);
            builder.ToTable(entity.EffectiveStorageName);

            var mapped = entity.EffectiveFields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var property in clr.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => !mapped.Contains(p.Name)))
                builder.Ignore(property.Name);

            foreach (var field in entity.EffectiveFields)
            {
                // A field may reference a column the entity does not list (metadata without a column catalog):
                // fall back to the field name rather than failing the whole model.
                var column = field.Column is { } c
                    ? entity.Columns.FirstOrDefault(x => x.Id.Equals(c.ColumnId))?.EffectiveStorageName ?? field.Name
                    : field.Name;
                builder.Property(field.Name).HasColumnName(column);
            }

            if (entity.PrimaryKey is { } pk
                && entity.EffectiveFields.FirstOrDefault(f => f.Column is { } c && c.ColumnId.Equals(pk.ColumnId))
                    is { } keyField)
                builder.HasKey(keyField.Name);
        }
    }
}

/// <summary>Builds Foundgine ORM entity maps from fluent metadata (no attributes on the model types).</summary>
public static class FluentOrmMaps
{
    [RequiresUnreferencedCode("Builds maps by reflection; supply generated maps for trimmed/AOT apps.")]
    [RequiresDynamicCode("Builds maps by reflection; supply generated maps for trimmed/AOT apps.")]
    public static IReadOnlyList<IOrmEntityMap> CreateAll(IEnumerable<EntityMetadata> entities) =>
        entities.Where(e => e.ClrType is not null)
            .Select(e => (IOrmEntityMap)Activator.CreateInstance(
                typeof(FluentOrmMap<>).MakeGenericType(e.ClrType!), e)!)
            .ToList();
}

/// <summary>Reflection-built ORM map for one entity type, driven by its fluent metadata.</summary>
public sealed class FluentOrmMap<[DynamicallyAccessedMembers(OrmEntityMembers.Required)] T> : OrmEntityMap<T>
    where T : class
{
    private sealed class Box<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }

    public FluentOrmMap(EntityMetadata entity) : base(entity.Name)
    {
        var add = typeof(FluentOrmMap<T>).GetMethod(nameof(AddField), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var field in entity.EffectiveFields)
        {
            var property = typeof(T).GetProperty(field.Name, BindingFlags.Public | BindingFlags.Instance)
                           ?? throw new InvalidOperationException($"{typeof(T).Name} has no member '{field.Name}'.");
            var isKey = entity.PrimaryKey is { } pk && field.Column is { } c && c.ColumnId.Equals(pk.ColumnId);
            add.MakeGenericMethod(property.PropertyType).Invoke(this, [field.Name, property, isKey]);
        }
    }

    private void AddField<TProp>(string name, PropertyInfo property, bool isKey)
    {
        var e = Expression.Parameter(typeof(T), "e");
        var body = Expression.Property(e, property);
        var typed = Expression.Lambda<Func<T, TProp>>(body, e);
        var get = typed.Compile();
        var selector = Expression.Lambda<Func<T, object?>>(Expression.Convert(body, typeof(object)), e);

        // The value sits behind a member access so EF parameterizes it instead of inlining a constant.
        Expression<Func<T, bool>> Compare(object? raw, Func<Expression, Expression, Expression> op)
        {
            var box = new Box<TProp>(OrmValue.Coerce<TProp>(raw));
            return Expression.Lambda<Func<T, bool>>(
                op(body, Expression.Property(Expression.Constant(box), nameof(Box<TProp>.Value))), e);
        }

        var type = Nullable.GetUnderlyingType(typeof(TProp)) ?? typeof(TProp);
        var orderable = type == typeof(int) || type == typeof(long) || type == typeof(decimal) ||
                        type == typeof(double) || type == typeof(DateTime) || type == typeof(string);

        Expression Gt(Expression l, Expression r) => type == typeof(string)
            ? Expression.GreaterThan(
                Expression.Call(typeof(string), nameof(string.Compare), null, l, r), Expression.Constant(0))
            : Expression.GreaterThan(l, r);

        Expression Lt(Expression l, Expression r) => type == typeof(string)
            ? Expression.LessThan(
                Expression.Call(typeof(string), nameof(string.Compare), null, l, r), Expression.Constant(0))
            : Expression.LessThan(l, r);

        Add(new OrmFieldBinding<T>(
            name, typeof(TProp), isKey,
            x => get(x),
            selector,
            v => Compare(v, Expression.Equal),
            v => Compare(v, Expression.NotEqual),
            vs =>
            {
                var list = new List<TProp>(vs.Count);
                foreach (var v in vs) list.Add(OrmValue.Coerce<TProp>(v));
                var source = Expression.Property(Expression.Constant(new Box<List<TProp>>(list)),
                    nameof(Box<List<TProp>>.Value));
                return Expression.Lambda<Func<T, bool>>(
                    Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(TProp)], source, body), e);
            },
            (q, desc) => desc ? Queryable.OrderByDescending(q, typed) : Queryable.OrderBy(q, typed),
            (q, desc) => desc ? Queryable.ThenByDescending(q, typed) : Queryable.ThenBy(q, typed),
            orderable ? v => Compare(v, Gt) : null,
            orderable ? v => Compare(v, Lt) : null,
            null)); // ORM-map reads only; writes go through the repository / EF directly
    }
}
