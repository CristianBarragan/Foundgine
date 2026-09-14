namespace Foundgine.Core.Execution.Mutation;

public interface IMutationExecutionProvider
{
    MutationResult Execute(
        ProviderMutationPlan plan,
        ExecutionContext context);

    MutationResult Execute(
        ProviderMutationPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Execute(plan, context);
    }
}
