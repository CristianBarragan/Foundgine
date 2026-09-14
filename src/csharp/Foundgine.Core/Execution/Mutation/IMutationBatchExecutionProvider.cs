namespace Foundgine.Core.Execution.Mutation;

/// <summary>
/// Executes an ordered mutation batch from the canonical provider-neutral
/// execution representation.
/// </summary>
public interface IMutationBatchExecutionProvider
{
    MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context);

    /// <summary>
    /// Cancellation-aware execution boundary. Providers must propagate the token
    /// to their physical command and roll back any transaction they own when
    /// cancellation interrupts execution. The default preserves compatibility
    /// for providers that do not yet implement cancellation-aware execution.
    /// </summary>
    MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteBatch(ir, context);
    }
}
