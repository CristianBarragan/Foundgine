using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;

namespace Foundgine.Core.Execution;

/// <summary>
/// Verifies that provider-bound execution artifacts retain the provenance of
/// the authorized semantic plan.
/// </summary>
public static class ExecutionIRBoundary
{
    public static void EnsureAuthorized(
        SemanticContractSnapshot contract,
        ExecutionIR ir,
        SemanticAuthorizationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(evidence);

        var binding = ir.AuthorizationBinding
                      ?? throw new InvalidOperationException(
                          "Execution IR is missing authorization provenance.");

        if (!string.Equals(binding.ContractFingerprint, contract.ContractFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Execution IR belongs to a different semantic contract.");

        if (!string.Equals(binding.AuthorizationFingerprint, evidence.AuthorizationFingerprint,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Execution IR belongs to a different authorization decision.");

        evidence.EnsureMatches(contract);
    }

    public static void BindProviderPlan(ExecutionIR ir, ProviderPlan providerPlan)
    {
        ArgumentNullException.ThrowIfNull(ir);
        ArgumentNullException.ThrowIfNull(providerPlan);

        var binding = ir.AuthorizationBinding
                      ?? throw new InvalidOperationException(
                          "Execution IR is missing authorization provenance.");

        providerPlan.BindAuthorization(binding);
    }

    public static void EnsureProviderPlan(
        SemanticContractSnapshot contract,
        ExecutionIR ir,
        ProviderPlan providerPlan,
        SemanticAuthorizationEvidence evidence)
    {
        EnsureAuthorized(contract, ir, evidence);
        ArgumentNullException.ThrowIfNull(providerPlan);

        if (providerPlan.AuthorizationBinding is null)
            throw new InvalidOperationException(
                "Provider plan is missing authorization provenance.");

        if (providerPlan.AuthorizationBinding != ir.AuthorizationBinding)
            throw new InvalidOperationException(
                "Provider plan authorization provenance does not match the execution IR.");

        providerPlan.AuthorizationBinding.EnsureMatches(contract, evidence);
    }
}