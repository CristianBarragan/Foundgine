using System.Diagnostics.CodeAnalysis;
using Foundgine.Providers.Orm;
using Microsoft.EntityFrameworkCore;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>EF Core binding of <see cref="IOrmQuerySource"/>: no-tracking queries over a <see cref="DbContext"/>.</summary>
public sealed class EfCoreQuerySource : IOrmQuerySource
{
    private readonly DbContext _context;

    public EfCoreQuerySource(DbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public IQueryable<TEntity> Query<[DynamicallyAccessedMembers(OrmEntityMembers.Required)] TEntity>() where TEntity : class =>
        _context.Set<TEntity>().AsNoTracking();

    public async Task<IReadOnlyList<TEntity>> ToListAsync<TEntity>(
        IQueryable<TEntity> query, CancellationToken cancellationToken) where TEntity : class =>
        await EntityFrameworkQueryableExtensions.ToListAsync(query, cancellationToken).ConfigureAwait(false);
}
