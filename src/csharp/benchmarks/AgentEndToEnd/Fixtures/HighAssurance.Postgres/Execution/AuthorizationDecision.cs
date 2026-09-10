using Foundgine.Core.Execution;
using Foundgine.HighAssurance.Banking;

namespace Foundgine.HighAssurance.Postgres.Execution;

/// <summary>
/// Versioned authorization evidence returned by a high-assurance authorization
/// decision. The evidence is execution-time data, not semantic plan identity.
/// </summary>
public sealed record AuthorizationDecision(
    bool Allowed,
    long Version,
    string Fingerprint)
{
    /// <summary>
    /// Creates deterministic compatibility evidence for the legacy boolean
    /// authorization callback. New authorization integrations should provide
    /// their own monotonic version and fingerprint.
    /// </summary>
    public static AuthorizationDecision FromBoolean(
        bool allowed,
        Guid actorId,
        BankAccount source,
        BankAccount destination)
    {
        var fingerprint = ExecutionEvidenceFactory.Hash(
            $"actor:{actorId}|source:{source.Id}|source-owner:{source.OwnerId}|source-tenant:{source.TenantId}" +
            $"|destination:{destination.Id}|destination-owner:{destination.OwnerId}|destination-tenant:{destination.TenantId}");

        return new AuthorizationDecision(allowed, 0, fingerprint);
    }
}