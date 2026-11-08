using Foundgine.Runtime;

namespace Foundgine.Providers.Gateway.Approvals;

/// <summary>Stores sensitive runtime approvals server-side. Implementations must provide atomic take/revoke semantics.</summary>
public interface IFoundgineGatewayApprovalStore
{
    ValueTask StoreAsync(string handleDigest, MutationPlanApproval approval, string subject, string? tenant,
        DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Atomically removes and returns an approval only when owner, tenant and expiry match.</summary>
    ValueTask<MutationPlanApproval?> TakeAsync(string handleDigest, string subject, string? tenant,
        DateTimeOffset now, CancellationToken cancellationToken = default);

    ValueTask<bool> RevokeAsync(string handleDigest, string subject, string? tenant,
        CancellationToken cancellationToken = default);
}
