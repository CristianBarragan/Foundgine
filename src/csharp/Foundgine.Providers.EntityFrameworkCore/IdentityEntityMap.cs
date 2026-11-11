#pragma warning disable IL2026, IL2060, IL2070, IL2075, IL3050

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Foundgine.Core.Semantic.Configuration;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>
/// Entity map for a type that is both the semantic model and the persistence type: every public scalar
/// property maps to itself. This removes the model -> storage mapping layer for the common case where the
/// domain class is the EF entity.
/// <code>model.Configure&lt;IdentityEntityMap&lt;Customer&gt;&gt;();</code>
/// Table, key and column names still come from a <see cref="FoundginePersistenceConfiguration{TEntity}"/> on the
/// same type; relationships come from a <see cref="FoundgineModelConfiguration{TModel}"/>.
/// </summary>
public sealed class IdentityEntityMap<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
    : FoundgineEntityMap<T, T> where T : class
{
    public override void Configure(FoundgineEntityMapBuilder<T, T> map)
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || property.GetMethod is null || !IsScalar(property.PropertyType))
                continue;
            typeof(IdentityEntityMap<T>)
                .GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(property.PropertyType)
                .Invoke(null, [map, property]);
        }
    }

    private static void Bind<TProperty>(FoundgineEntityMapBuilder<T, T> map, PropertyInfo property)
    {
        var x = Expression.Parameter(typeof(T), "x");
        var selector = Expression.Lambda<Func<T, TProperty>>(Expression.Property(x, property), x);
        map.Property(selector).To(selector);
    }

    internal static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(TimeOnly) ||
               type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(TimeSpan);
    }
}
