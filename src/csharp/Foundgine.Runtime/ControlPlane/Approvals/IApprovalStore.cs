namespace Foundgine.Runtime.ControlPlane.Approvals;

public interface IApprovalStore
{
    ApprovalRequest Create(string requestFingerprint, int requiredApprovals = 1);

    bool TryGet(string approvalId, out ApprovalRequest? request);

    ApprovalRequest RecordDecision(string approvalId, ApprovalGrant grant);
}

/// <summary>
/// Process-local approval store. A production deployment with real
/// human-in-the-loop approvers should back <see cref="IApprovalStore"/>
/// with durable storage — pending approvals must survive a process
/// restart — but the interface and workflow shape stay the same.
/// </summary>
public sealed class InMemoryApprovalStore : IApprovalStore
{
    private readonly Dictionary<string, ApprovalRequest> _requests = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ApprovalRequest Create(string requestFingerprint, int requiredApprovals = 1)
    {
        var request = ApprovalRequest.Create(Guid.NewGuid().ToString("n"), requestFingerprint, requiredApprovals);
        lock (_gate)
        {
            _requests[request.ApprovalId] = request;
        }

        return request;
    }

    public bool TryGet(string approvalId, out ApprovalRequest? request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        lock (_gate)
        {
            return _requests.TryGetValue(approvalId, out request);
        }
    }

    public ApprovalRequest RecordDecision(string approvalId, ApprovalGrant grant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentNullException.ThrowIfNull(grant);

        lock (_gate)
        {
            if (!_requests.TryGetValue(approvalId, out var existing))
                throw new KeyNotFoundException($"No approval request '{approvalId}' exists.");

            var updated = existing.WithGrant(grant);
            _requests[approvalId] = updated;
            return updated;
        }
    }
}