using Foundgine.Core.Execution.Mutation;
using Foundgine.Providers.Orm;
using Microsoft.EntityFrameworkCore;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.EntityFrameworkCore;

/// <summary>EF Core binding of <see cref="IOrmMutationSource"/>: tracked queries and the unit of work of a <see cref="DbContext"/>.</summary>
public sealed class EfCoreMutationSource : IOrmMutationSource
{
    private readonly DbContext _context;

    public EfCoreMutationSource(DbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public IOrmTransaction BeginTransaction()
    {
        var ambient = _context.Database.CurrentTransaction;
        return ambient is not null
            ? new JoinedTransaction(ambient, _context)
            : new OwnedTransaction(_context.Database.BeginTransaction(), _context);
    }

    public IQueryable<TEntity> TrackedQuery<TEntity>() where TEntity : class => _context.Set<TEntity>();
    public void Add<TEntity>(TEntity entity) where TEntity : class => _context.Set<TEntity>().Add(entity);
    public void Remove<TEntity>(TEntity entity) where TEntity : class => _context.Set<TEntity>().Remove(entity);
    public int SaveChanges() => _context.SaveChanges();

    private sealed class OwnedTransaction(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner,
        DbContext context) : IOrmTransaction
    {
        public void Commit() => inner.Commit();
        public void Rollback()
        {
            try { inner.Rollback(); }
            finally { context.ChangeTracker.Clear(); }
        }
        public void Dispose() => inner.Dispose(); // disposing an uncommitted EF transaction rolls it back
    }

    /// <summary>
    /// The application retains commit ownership, but a failed Foundgine batch must still abort the ambient
    /// transaction. A no-op rollback would let a caller accidentally commit a partially applied batch.
    /// </summary>
    private sealed class JoinedTransaction(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner,
        DbContext context) : IOrmTransaction
    {
        public void Commit() { } // only the transaction owner may commit
        public void Rollback()
        {
            try { inner.Rollback(); } // failure poisons the caller's whole transaction
            finally { context.ChangeTracker.Clear(); }
        }
        public void Dispose() { } // ownership remains with the caller
    }
}

/// <summary>Executes mutation batches against a <see cref="DbContext"/> inside one transaction.</summary>
public sealed class EfCoreMutationExecutionProvider : IMutationBatchExecutionProvider, IMutationSecurityConformanceEvaluator
{
    private readonly OrmMutationExecutionProvider _inner;

    public EfCoreMutationExecutionProvider(DbContext context, OrmRegistry registry) =>
        _inner = new OrmMutationExecutionProvider(FoundgineEfCore.ProviderName, new EfCoreMutationSource(context), registry);

    public MutationBatchResult ExecuteBatch(ExecutionMutationIR ir, ExecutionContext context) =>
        _inner.ExecuteBatch(ir, context);

    public MutationBatchResult ExecuteBatch(ExecutionMutationIR ir, ExecutionContext context, CancellationToken cancellationToken) =>
        _inner.ExecuteBatch(ir, context, cancellationToken);

    public MutationSecurityConformanceResult Evaluate(ExecutionMutationIR ir) => _inner.Evaluate(ir);
}
