namespace Foundgine.Runtime.ControlPlane.Approvals;

/// <summary>Lifecycle state of a human-in-the-loop approval request.</summary>
public enum ApprovalStatus
{
    Pending,
    Granted,
    Denied,
    Expired,
}

/// <summary>A single grant or denial recorded against an approval request.</summary>
public sealed record ApprovalGrant(string ApproverId, bool Granted, string? Comment, DateTimeOffset DecidedAt);

/// <summary>
/// A pending human sign-off for a tool call that policy flagged with
/// <see cref="Foundgine.Runtime.ControlPlane.PolicyGateway.PolicyOutcome.RequireApproval"/>.
/// This is intentionally distinct from <c>Foundgine.Core.PlanApproval</c>:
/// that type binds an execution to an exact plan fingerprint at the moment
/// of execution; this type is the upstream human workflow that decides
/// whether the call may proceed at all. The two compose — a governor may
/// require an <see cref="ApprovalRequest"/> to reach <see cref="ApprovalStatus.Granted"/>
/// before a <c>PlanApproval</c> is ever created.
/// </summary>
public sealed record ApprovalRequest(
    string ApprovalId,
    string RequestFingerprint,
    int RequiredApprovals,
    ApprovalStatus Status,
    IReadOnlyList<ApprovalGrant> Grants)
{
    public static ApprovalRequest Create(string approvalId, string requestFingerprint, int requiredApprovals = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        if (requiredApprovals < 1)
            throw new ArgumentOutOfRangeException(nameof(requiredApprovals), "At least one approval must be required.");

        return new ApprovalRequest(approvalId, requestFingerprint, requiredApprovals, ApprovalStatus.Pending,
            Array.Empty<ApprovalGrant>());
    }

    public ApprovalRequest WithGrant(ApprovalGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (Status is not ApprovalStatus.Pending)
            throw new InvalidOperationException(
                $"Approval request '{ApprovalId}' is already '{Status}' and cannot accept further decisions.");

        List<ApprovalGrant> grants = [.. Grants, grant];

        if (!grant.Granted)
            return this with { Status = ApprovalStatus.Denied, Grants = grants };

        var granted = grants.Count(g => g.Granted) >= RequiredApprovals;
        return this with { Status = granted ? ApprovalStatus.Granted : ApprovalStatus.Pending, Grants = grants };
    }
}