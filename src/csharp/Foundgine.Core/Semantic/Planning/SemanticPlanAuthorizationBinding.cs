using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;

namespace Foundgine.Core.Semantic.Planning;

/// <summary>
/// Immutable provenance binding for a semantic plan. A bound plan may only be
/// used as the product of authorization performed against the same contract
/// and authorization decision.
/// </summary>
public sealed record SemanticPlanAuthorizationBinding(
    string ContractFingerprint,
    string AuthorizationFingerprint)
{
    public static SemanticPlanAuthorizationBinding Create(
        SemanticContractSnapshot contract,
        SemanticAuthorizationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.EnsureMatches(contract);
        return new SemanticPlanAuthorizationBinding(
            contract.ContractFingerprint,
            evidence.AuthorizationFingerprint);
    }

    public void EnsureMatches(
        SemanticContractSnapshot contract,
        SemanticAuthorizationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidence);

        if (!string.Equals(ContractFingerprint, contract.ContractFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Semantic plan is bound to contract '{ContractFingerprint}', but the supplied contract is '{contract.ContractFingerprint}'.");

        if (!string.Equals(AuthorizationFingerprint, evidence.AuthorizationFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Semantic plan authorization evidence does not match the authorization decision that produced the plan.");

        evidence.EnsureMatches(contract);
    }
}