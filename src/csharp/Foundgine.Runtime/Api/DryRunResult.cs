using Foundgine.Core.Semantic.Planning;

namespace Foundgine.Runtime;

/// <summary>
/// Result of planning an intent without executing it. The returned fingerprint
/// identifies the exact authorized plan that was inspected.
/// </summary>
public sealed record DryRunResult(
    PlanInspection Inspection,
    bool ExecutionRequiredApproval = false);