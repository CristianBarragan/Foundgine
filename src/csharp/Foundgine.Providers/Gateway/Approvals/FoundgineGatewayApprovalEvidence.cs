using Foundgine.Runtime;

namespace Foundgine.Providers.Gateway.Approvals;

/// <summary>Non-secret evidence committed with an approval and its outbox notification.</summary>
public sealed record FoundgineGatewayApprovalEvidence(
    string EventId, string EventType, string Subject, string? Tenant,
    string PlanFingerprint, DateTimeOffset OccurredAt, DateTimeOffset ExpiresAt);

/// <summary>
/// Stronger approval-store contract: approval state and its audit/outbox evidence must commit atomically
/// in one storage transaction. Implementations must not publish an approval if evidence persistence fails.
/// </summary>
public interface IAtomicFoundgineGatewayApprovalStore : IFoundgineGatewayApprovalStore
{
    ValueTask StoreWithEvidenceAsync(string handleDigest, MutationPlanApproval approval,
        FoundgineGatewayApprovalEvidence evidence, CancellationToken cancellationToken = default);
}
