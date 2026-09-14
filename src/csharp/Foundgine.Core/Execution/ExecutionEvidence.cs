using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Core.Execution;

/// <summary>
/// Provider-neutral provenance for one execution. Evidence describes what was
/// executed and which authorization boundaries were present without retaining
/// request objects, expression trees, or provider-specific runtime state.
/// </summary>
public sealed record ExecutionEvidence(
    string Provider,
    string PlanFingerprint,
    IReadOnlyList<int> AuthorizedNodeIds,
    int RowsReturned,
    long ElapsedMilliseconds,
    string? ProviderOperationFingerprint = null,
    string? IntentFingerprint = null,
    string? AuthorizationFingerprint = null,
    string? WarrantId = null,
    string? WarrantDigest = null,
    string? SecurityInvariantDigest = null,
    long? AuthorizationVersion = null);

public static class ExecutionEvidenceFactory
{
    public static ExecutionEvidence Create(
        string provider,
        string planFingerprint,
        IEnumerable<int> authorizedNodeIds,
        int rowsReturned,
        long elapsedMilliseconds,
        string? providerOperation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(planFingerprint);

        return new ExecutionEvidence(
            provider,
            planFingerprint,
            authorizedNodeIds.OrderBy(x => x).ToArray(),
            rowsReturned,
            elapsedMilliseconds,
            providerOperation is null ? null : Hash(providerOperation));
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}