using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>
/// Minimal generic CRUD over an EF entity. Mechanical by design: it holds no authorization. Foundgine authorizes
/// semantic operations before they reach the repository; the application facade authorizes capabilities.
/// Anything beyond CRUD is one call away: <see cref="Query"/> for LINQ (joins, projections, set-based
/// <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c>) and <see cref="Context"/> for raw EF.
/// </summary>
public interface IFoundgineRepository<[DynamicallyAccessedMembers(FoundgineEfOptions.EfEntityMembers)] T> where T : class
{
    /// <summary>The underlying context (transactions, raw SQL, change tracker).</summary>
    DbContext Context { get; }

    /// <summary>No-tracking query over the entity set.</summary>
    IQueryable<T> Query();

    Task<T?> GetAsync(object id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    /// <summary>Inserts the entity and returns it with its generated key. The entity is detached afterwards.</summary>
    Task<T> AddAsync(T entity, CancellationToken ct = default);

    /// <summary>Replaces every column of the row with the entity's values (key taken from the entity).</summary>
    Task<T> UpdateAsync(T entity, CancellationToken ct = default);

    /// <summary>Sets one property of the row with this key. Returns the rows affected (0 when the key is unknown).</summary>
    Task<int> UpdateAsync<TValue>(object id, Expression<Func<T, TValue>> property, TValue value,
        CancellationToken ct = default);

    /// <summary>Sets named properties of the row with this key (used by the generic dynamic pipeline).</summary>
    Task<int> UpdateAsync(object id, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default);

    /// <summary>Deletes the row with this key. Returns the rows affected.</summary>
    Task<int> DeleteAsync(object id, CancellationToken ct = default);
}

/// <summary>EF Core implementation of <see cref="IFoundgineRepository{T}"/>. Virtual members allow extension.</summary>
public class FoundgineEfRepository<[DynamicallyAccessedMembers(FoundgineEfOptions.EfEntityMembers)] T>(
    FoundgineDbContext context) : IFoundgineRepository<T> where T : class
{
    public DbContext Context => context;

    public virtual IQueryable<T> Query() => context.Set<T>().AsNoTracking();

    public virtual async Task<T?> GetAsync(object id, CancellationToken ct = default)
    {
        var entity = await context.Set<T>().FindAsync([id], ct);
        if (entity is not null) context.Entry(entity).State = EntityState.Detached;
        return entity;
    }

    public virtual async Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        var query = Query();
        if (predicate is not null) query = query.Where(predicate);
        return await query.ToListAsync(ct);
    }

    public virtual Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Query().AnyAsync(predicate, ct);

    public virtual Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) =>
        predicate is null ? Query().CountAsync(ct) : Query().CountAsync(predicate, ct);

    public virtual async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        context.Set<T>().Add(entity);
        try
        {
            await context.SaveChangesAsync(ct);
        }
        finally
        {
            context.Entry(entity).State = EntityState.Detached; // a failed insert must not stay tracked
        }

        return entity;
    }

    public virtual async Task<T> UpdateAsync(T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        context.Set<T>().Update(entity);
        try
        {
            await context.SaveChangesAsync(ct);
        }
        finally
        {
            context.Entry(entity).State = EntityState.Detached;
        }

        return entity;
    }

    public virtual async Task<int> UpdateAsync<TValue>(object id, Expression<Func<T, TValue>> property, TValue value,
        CancellationToken ct = default)
    {
        var name = property.Body switch
        {
            MemberExpression m => m.Member.Name,
            UnaryExpression { Operand: MemberExpression m } => m.Member.Name,
            _ => throw new ArgumentException("Select a single property, for example x => x.Status.", nameof(property))
        };
        return await UpdateAsync(id, new Dictionary<string, object?> { [name] = value }, ct);
    }

    public virtual async Task<int> UpdateAsync(object id, IReadOnlyDictionary<string, object?> values,
        CancellationToken ct = default)
    {
        var entity = await context.Set<T>().FindAsync([id], ct);
        if (entity is null) return 0;
        try
        {
            var entry = context.Entry(entity);
            foreach (var (name, value) in values) entry.Property(name).CurrentValue = value;
            return await context.SaveChangesAsync(ct);
        }
        finally
        {
            context.Entry(entity).State = EntityState.Detached;
        }
    }

    public virtual async Task<int> DeleteAsync(object id, CancellationToken ct = default)
    {
        var entity = await context.Set<T>().FindAsync([id], ct);
        if (entity is null) return 0;
        context.Set<T>().Remove(entity);
        try
        {
            return await context.SaveChangesAsync(ct);
        }
        finally
        {
            context.Entry(entity).State = EntityState.Detached;
        }
    }
}
