using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.BenchmarkApi;

/// <summary>
/// Singleton-safe EF mutation provider. Each batch gets a fresh DbContext from the
/// factory, while the underlying EF provider owns one transaction for the full batch.
/// </summary>
public sealed class FactoryEfCoreMutationExecutionProvider(
    IDbContextFactory<FoundgineDbContext> contexts,
    FoundgineEfModel model)
    : IMutationBatchExecutionProvider, IMutationSecurityConformanceEvaluator
{
    public MutationBatchResult ExecuteBatch(ExecutionMutationIR ir, ExecutionContext context) =>
        ExecuteBatch(ir, context, CancellationToken.None);

    public MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        using var db = contexts.CreateDbContext();
        var provider = new EfCoreMutationExecutionProvider(db, model.Registry);
        return provider.ExecuteBatch(ir, context, cancellationToken);
    }

    public MutationSecurityConformanceResult Evaluate(ExecutionMutationIR ir)
    {
        using var db = contexts.CreateDbContext();
        return new EfCoreMutationExecutionProvider(db, model.Registry).Evaluate(ir);
    }
}
