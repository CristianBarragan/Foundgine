using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;

namespace Foundgine.Core.Execution;

/// <summary>
/// Current authorization authority state observed immediately before provider execution.
/// A state change invalidates previously issued authorization evidence.
/// </summary>
public sealed record ExecutionAuthorizationAuthorityState(
    long Version,
    string Fingerprint,
    bool Allowed = true);

/// <summary>
/// Revalidates authorization at the final execution boundary. Implementations may
/// consult a database, distributed authority, cache, or another trusted control plane.
/// </summary>
public interface IExecutionAuthorizationRevalidator
{
    ValueTask ValidateAsync(
        SemanticContractSnapshot contract,
        SemanticAuthorizationEvidence evidence,
        ExecutionAuthorizationAuthorityState? currentAuthority,
        CancellationToken cancellationToken = default);
}

/// <summary>Default fail-closed execution revalidator for contract-bound evidence.</summary>
public sealed class SemanticExecutionAuthorizationRevalidator : IExecutionAuthorizationRevalidator
{
    public ValueTask ValidateAsync(
        SemanticContractSnapshot contract,
        SemanticAuthorizationEvidence evidence,
        ExecutionAuthorizationAuthorityState? currentAuthority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();

        evidence.EnsureMatches(contract);

        if (currentAuthority is not null)
        {
            if (!currentAuthority.Allowed)
                throw new UnauthorizedAccessException(
                    "The current authorization authority is revoked; execution fails closed.");

            if (string.IsNullOrWhiteSpace(currentAuthority.Fingerprint))
                throw new InvalidOperationException(
                    "The current authorization authority fingerprint is missing; execution fails closed.");

            if (evidence.AuthorizationVersion is null)
                throw new InvalidOperationException(
                    "Authorization evidence has no authority version and cannot be revalidated against current authority.");

            if (evidence.AuthorizationVersion.Value != currentAuthority.Version)
                throw new InvalidOperationException(
                    $"Authorization evidence version {evidence.AuthorizationVersion.Value} is no longer current; " +
                    $"current authority version is {currentAuthority.Version}. Execution fails closed.");

            if (evidence.AuthorizationAuthorityFingerprint is null ||
                !string.Equals(
                    evidence.AuthorizationAuthorityFingerprint,
                    currentAuthority.Fingerprint,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Authorization evidence does not match the current authorization authority. Execution fails closed.");
        }

        return ValueTask.CompletedTask;
    }
}