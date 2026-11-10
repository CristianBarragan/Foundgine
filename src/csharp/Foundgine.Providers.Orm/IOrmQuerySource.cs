using System.Diagnostics.CodeAnalysis;
namespace Foundgine.Providers.Orm;

/// <summary>
/// The only seam between the shared composer and a concrete ORM. Everything above it is plain
/// <see cref="IQueryable{T}"/>; The EF Core adapter implements these two members.
/// </summary>
public interface IOrmQuerySource
{
    /// <summary>A read-only (no change tracking) root query for the entity type.</summary>
    IQueryable<TEntity> Query<[DynamicallyAccessedMembers(OrmEntityMembers.Required)] TEntity>() where TEntity : class;

    Task<IReadOnlyList<TEntity>> ToListAsync<TEntity>(IQueryable<TEntity> query, CancellationToken cancellationToken)
        where TEntity : class;
}
