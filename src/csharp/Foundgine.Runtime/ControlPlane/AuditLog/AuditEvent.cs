namespace Foundgine.Runtime.ControlPlane.AuditLog;

/// <summary>The stage of tool-call governance an audit event records.</summary>
public enum AuditCategory
{
    RiskScored,
    PolicyEvaluated,
    ApprovalRequested,
    ApprovalDecided,
    Routed,
    Denied,
}

/// <summary>
/// An immutable record of one governance step for one tool call. Events
/// carry a fingerprint rather than raw request/result payloads, the same
/// evidence-not-payload discipline used by
/// <see cref="Foundgine.Core.Execution.ExecutionReceipt"/>, so the audit
/// log can be retained and shipped without duplicating domain data.
/// </summary>
public sealed record AuditEvent(
    AuditCategory Category,
    string ToolName,
    string Actor,
    string? Tenant,
    string Fingerprint,
    string Summary,
    DateTimeOffset OccurredAt)
{
    public static AuditEvent Create(
        AuditCategory category,
        string toolName,
        string actor,
        string? tenant,
        string fingerprint,
        string summary) =>
        new(category, toolName, actor, tenant, fingerprint, summary, DateTimeOffset.UtcNow);
}
