using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning.Mutation;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

public sealed class ExecutionMutationIRTests
{
    [Fact]
    public void CompilesMutationBatchIntoCanonicalExecutionIR()
    {
        var entityId = new EntityId(1);
        var fieldId = new FieldId(1);
        var columnId = new ColumnId(1);

        var entity = new MutationEntitySchema(
            entityId,
            "Customer",
            new HashSet<ColumnId> { columnId },
            new Dictionary<FieldId, ColumnId?> { [fieldId] = columnId },
            columnId);

        var operation = new MutationOperation(
            entity,
            MutationKind.Create,
            new[]
            {
                new MutationFieldValue(columnId, "Alice")
            },
            null,
            null,
            new[] { fieldId });

        var plan = new MutationBatchPlan(
            new[] { operation },
            Array.Empty<MutationDependency>());

        var ir = ExecutionMutationIRCompiler.Compile(plan);

        Assert.Single(ir.Operations);
        Assert.Same(operation, ir.Operations[0]);
        Assert.Empty(ir.Dependencies);
    }

    [Fact]
    public void RejectsDependencyThatDoesNotPointToAnEarlierOperation()
    {
        var entityId = new EntityId(1);
        var fieldId = new FieldId(1);
        var columnId = new ColumnId(1);

        var entity = new MutationEntitySchema(
            entityId,
            "Customer",
            new HashSet<ColumnId> { columnId },
            new Dictionary<FieldId, ColumnId?> { [fieldId] = columnId },
            columnId);

        var operation = new MutationOperation(
            entity,
            MutationKind.Create,
            new[]
            {
                new MutationFieldValue(columnId, "Alice")
            },
            null,
            null,
            new[] { fieldId });

        var dependency = new MutationDependency(
            0,
            0,
            fieldId,
            columnId);

        var plan = new MutationBatchPlan(
            new[] { operation },
            new[] { dependency });

        Assert.Throws<InvalidOperationException>(() => ExecutionMutationIRCompiler.Compile(plan));
    }

    [Fact]
    public void ExecutionMutationIRIsTheProviderBatchContract()
    {
        var interfaceType = typeof(IMutationBatchExecutionProvider);
        var method = interfaceType.GetMethod(
            nameof(IMutationBatchExecutionProvider.ExecuteBatch),
            new[] { typeof(ExecutionMutationIR), typeof(ExecutionContext) });

        Assert.NotNull(method);
        Assert.Equal(typeof(MutationBatchResult), method!.ReturnType);
    }
}