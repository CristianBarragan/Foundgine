using System.Linq.Expressions;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.EntityFrameworkCore;

namespace Foundgine.SupplyChain.Application.Querying;

/// <summary>
/// Lambda entry point for semantic reads:
/// <c>reader.From&lt;SalesOrder&gt;().Eq(o =&gt; o.CustomerId, 1).Include(o =&gt; o.Lines).OrderBy(o =&gt; o.Id).ReadAsync(ct)</c>.
/// The read still goes through <see cref="FoundgineEfReader"/> (authorization, planning, EF execution).
/// </summary>
public sealed class SupplyChainReader(FoundgineEfReader reader)
{
    public Query<T> From<T>() where T : class => new(reader);
}

public sealed class Query<T> where T : class
{
    private readonly FoundgineEfReader _reader;
    private readonly SemanticQuery _query;

    internal Query(FoundgineEfReader reader)
    {
        _reader = reader;
        _query = reader.From(EntityId.Create(typeof(T).Name));
    }

    public Query<T> Eq<TValue>(Expression<Func<T, TValue>> field, TValue value) => Where(Filter<T>.Eq(field, value));

    public Query<T> Where(params SemanticFilterExpression[] conditions)
    {
        _query.Where(conditions);
        return this;
    }

    public Query<T> OrderBy<TValue>(Expression<Func<T, TValue>> field)
    {
        _query.OrderBy(new SemanticOrderTerm(Filter<T>.Field(field), SemanticSortDirection.Asc));
        return this;
    }

    public Query<T> OrderByDescending<TValue>(Expression<Func<T, TValue>> field)
    {
        _query.OrderBy(new SemanticOrderTerm(Filter<T>.Field(field), SemanticSortDirection.Desc));
        return this;
    }

    public Query<T> Take(int limit)
    {
        _query.Take(limit);
        return this;
    }

    /// <summary>Loads a related collection or reference: <c>.Include(o =&gt; o.Lines)</c>.</summary>
    public Query<T> Include<TValue>(Expression<Func<T, TValue>> navigation)
    {
        _query.Include(Filter<T>.Relationship(navigation));
        return this;
    }

    public Task<FoundgineReadResult> ReadAsync(CancellationToken ct) => _reader.ReadAsync(_query, ct);

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ToRowsAsync(CancellationToken ct) =>
        (await ReadAsync(ct)).Rows.Select(r => (IReadOnlyDictionary<string, object?>)r.Values).ToArray();
}
