using Foundgine.Core.Semantic;

namespace Foundgine.Runtime;

/// <summary>
/// Approval bound to the exact authorized semantic plan represented by the
/// fingerprint. It is not an authorization grant and cannot be reused for a
/// different plan.
/// </summary>
public sealed record PlanApproval(
    SemanticRequest Request,
    string ApprovalId,
    string PlanFingerprint,
    string SemanticModelVersion,
    int CapabilityContractVersion,
    int CapabilityVersion,
    int IntentVersion,
    int PlanVersion,
    string ApprovedBy,
    DateTimeOffset ApprovedAt);