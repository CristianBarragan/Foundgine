package com.foundgine.runtime.controlplane.toolregistry;

import com.foundgine.runtime.controlplane.riskscoring.RiskScore;

import java.util.List;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.ToolRegistry.ToolDescriptor},
 * together with the {@code ToolStatus} enum declared in the same C# file.
 *
 * <p>Declares that a tool exists and what it's capable of, independent of any
 * particular call. This is the governance-side record; the actual callable
 * implementation lives in {@code Foundgine.Providers.Tools.MCP} (not yet
 * ported) — the registry does not invoke tools, it only describes them.
 */
public record ToolDescriptor(
        String toolName,
        List<String> capabilities,
        RiskScore.RiskTier defaultRiskTier,
        ToolStatus status) {

    /** Lifecycle status of a registered tool. */
    public enum ToolStatus {
        ACTIVE, DEPRECATED, DISABLED
    }

    public ToolDescriptor {
        capabilities = List.copyOf(capabilities);
    }
}
