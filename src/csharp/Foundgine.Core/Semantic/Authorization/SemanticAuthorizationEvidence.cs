using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundgine.Core.Semantic.IR;

namespace Foundgine.Core.Semantic.Authorization;

/// <summary>
/// Immutable authorization evidence bound to the exact semantic contract that
/// was evaluated. Evidence from one contract cannot be replayed against another.
/// </summary>
public sealed record SemanticAuthorizationEvidence(
    string ContractFingerprint,
    string AuthorizationFingerprint,
    long? AuthorizationVersion = null,
    string? AuthorizationAuthorityFingerprint = null)
{
    public void EnsureMatches(SemanticContractSnapshot contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!string.Equals(ContractFingerprint, contract.ContractFingerprint, StringComparison.Ordinal))
            throw new SemanticAuthorizationException(
                $"Authorization evidence is bound to semantic contract '{ContractFingerprint}', " +
                $"but the supplied contract is '{contract.ContractFingerprint}'.");
    }

    public static SemanticAuthorizationEvidence Create(
        SemanticContractSnapshot contract,
        SemanticOperation authorizedOperation)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(authorizedOperation);

        var canonical = JsonSerializer.Serialize(authorizedOperation);
        var payload = $"contract={contract.ContractFingerprint}|operation={canonical}";
        return new SemanticAuthorizationEvidence(
            contract.ContractFingerprint,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
    }
}