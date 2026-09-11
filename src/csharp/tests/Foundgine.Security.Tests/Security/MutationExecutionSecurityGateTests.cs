using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Security;
using Xunit;

namespace Foundgine.Security.Tests.Security;

public sealed class MutationExecutionSecurityGateTests
{
    [Fact]
    public void Certificate_is_bound_to_exact_mutation_ir_and_provider_instance()
    {
        var ir = TestIr(SecurityInvariantIds.AuthorizationRequired);
        var provider = new HonestMutationProvider();
        var certificate = MutationExecutionSecurityGate.Certify(
            ir,
            provider,
            provider.GetType().FullName!,
            [SecurityInvariantIds.AuthorizationRequired]);

        MutationExecutionSecurityGate.EnsureExecutable(ir, provider, certificate);

        var clonedIr = ir with { RequiredSecurityInvariants = ir.RequiredSecurityInvariants.ToArray() };
        var irException = Assert.Throws<InvalidOperationException>(() =>
            MutationExecutionSecurityGate.EnsureExecutable(clonedIr, provider, certificate));
        Assert.Contains("exact mutation IR", irException.Message, StringComparison.OrdinalIgnoreCase);

        var providerException = Assert.Throws<InvalidOperationException>(() =>
            MutationExecutionSecurityGate.EnsureExecutable(ir, new HonestMutationProvider(), certificate));
        Assert.Contains("exact mutation IR and provider", providerException.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_without_concrete_evaluator_cannot_satisfy_provider_owned_invariant()
    {
        var ir = TestIr(SecurityInvariantIds.ParameterizedValues);
        var provider = new UncertifiedMutationProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MutationExecutionSecurityGate.Certify(
                ir,
                provider,
                provider.GetType().FullName!,
                []));

        Assert.Contains("no concrete security conformance evaluator", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_conformance_is_combined_with_engine_authorization_evidence()
    {
        var ir = TestIr(
            SecurityInvariantIds.AuthorizationRequired,
            SecurityInvariantIds.ParameterizedValues);
        var provider = new HonestMutationProvider();

        var certificate = MutationExecutionSecurityGate.Certify(
            ir,
            provider,
            provider.GetType().FullName!,
            [SecurityInvariantIds.AuthorizationRequired]);

        Assert.True(certificate.IsSatisfied);
        Assert.Contains(SecurityInvariantIds.AuthorizationRequired, certificate.Preserved);
        Assert.Contains(SecurityInvariantIds.ParameterizedValues, certificate.Preserved);
    }

    private static ExecutionMutationIR TestIr(params string[] required)
    {
        var entityId = new EntityId(1);
        var fieldId = new FieldId(2);
        var columnId = new ColumnId(3);
        var entity = new MutationEntitySchema(
            entityId,
            "Entity",
            new HashSet<ColumnId> { columnId },
            new Dictionary<FieldId, ColumnId?> { [fieldId] = columnId },
            columnId);

        var operation = new MutationOperation(
            entity,
            MutationKind.Create,
            [new MutationFieldValue(columnId, "value")],
            null,
            null,
            [fieldId]);

        return ExecutionMutationIR.From(
            new MutationBatchPlan([operation], []),
            required);
    }

    private sealed class HonestMutationProvider : IMutationSecurityConformanceEvaluator
    {
        public MutationSecurityConformanceResult Evaluate(ExecutionMutationIR ir) =>
            new(
                GetType().FullName!,
                [SecurityInvariantIds.ParameterizedValues],
                []);
    }

    private sealed class UncertifiedMutationProvider
    {
    }
}