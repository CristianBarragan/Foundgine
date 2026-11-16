namespace Foundgine.Providers.AgentFramework.Hosting;

/// <summary>Configuration for the Foundgine Microsoft Agent Framework adapter.</summary>
public sealed class FoundgineAgentFrameworkOptions
{
    /// <summary>Agent name surfaced to MAF (telemetry, workflows). Defaults to <c>foundgine-agent</c>.</summary>
    public string AgentName { get; set; } = "foundgine-agent";

    /// <summary>Optional description of the agent for MAF orchestration.</summary>
    public string? AgentDescription { get; set; } =
        "Answers questions over application data through the Foundgine semantic execution boundary.";

    /// <summary>Overrides the default system instructions. Null keeps <see cref="Foundgine.Providers.AgentFramework.Agents.FoundgineAgentInstructions.Default"/>.</summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Exposes mutation tools. Off by default: an agent is read-only unless the host opts in. When on, execution is
    /// always gated by MAF tool approval; there is deliberately no switch to run mutations without a human decision.
    /// </summary>
    public bool EnableMutations { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(AgentName))
            throw new InvalidOperationException($"{nameof(AgentName)} is required.");
    }
}
