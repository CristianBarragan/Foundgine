using Foundgine.Runtime;
using Foundgine.Runtime.ControlPlane;

namespace Foundgine.Runtime.Capabilities;

/// <summary>
/// Turns on the tool-call governance control plane's audit trail: registers
/// <see cref="ToolGovernanceServiceCollectionExtensions.AddFoundgineToolGovernance"/>'s in-memory
/// <c>IAuditLog</c> and the rest of the governance pipeline (risk scoring, policy gateway, approvals,
/// routing), so every routed, risk-scored, policy-evaluated, or denied tool call leaves an
/// <c>AuditEvent</c> fingerprint rather than a raw request/result payload - the same
/// evidence-not-payload discipline as <see cref="Foundgine.Core.Execution.ExecutionReceipt"/>.
///
/// The in-memory <c>IAuditLog</c> registered here does not survive a restart. Replace it (and any
/// other governance registration) with a durable implementation after enabling this capability if
/// audit evidence needs to outlive the process - the last registration for a given service wins.
/// </summary>
public sealed class AuditEvidence : IFoundgineCapability
{
    public static void Configure(FoundgineCapabilityContext context) =>
        context.Services.AddFoundgineToolGovernance();
}

/// <summary>Fluent <c>Use</c>/<c>Disable</c> surface for <see cref="AuditEvidence"/>.</summary>
public static class AuditEvidenceFoundgineOptionsExtensions
{
    /// <summary>Enables <see cref="AuditEvidence"/>. Equivalent to <c>options.Enable&lt;AuditEvidence&gt;()</c>.</summary>
    public static FoundgineOptions UseAuditEvidence(this FoundgineOptions options) =>
        options.Enable<AuditEvidence>();

    public static FoundgineOptions DisableAuditEvidence(this FoundgineOptions options) =>
        options.Disable<AuditEvidence>();
}
