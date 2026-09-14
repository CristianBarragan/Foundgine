using Foundgine.Runtime.ControlPlane.RiskScoring;

namespace Foundgine.Runtime.ControlPlane.ToolRegistry;

/// <summary>Lifecycle status of a registered tool.</summary>
public enum ToolStatus
{
    Active,
    Deprecated,
    Disabled,
}

/// <summary>
/// Declares that a tool exists and what it's capable of, independent of any
/// particular call. This is the governance-side record; the actual callable
/// implementation lives in <c>Foundgine.Providers.Tools.MCP</c> — the
/// registry does not invoke tools, it only describes them.
/// </summary>
public sealed record ToolDescriptor(
    string ToolName,
    IReadOnlyList<string> Capabilities,
    RiskTier DefaultRiskTier,
    ToolStatus Status);
