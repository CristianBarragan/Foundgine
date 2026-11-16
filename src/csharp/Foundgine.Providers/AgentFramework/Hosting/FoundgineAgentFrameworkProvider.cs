using Foundgine.Providers.Gateway.Providers;

namespace Foundgine.Providers.AgentFramework.Hosting;

/// <summary>
/// Declares Microsoft Agent Framework as a Foundgine <see cref="FoundgineProviderKind.Agent"/> provider so
/// startup negotiation and gateway discovery can see it.
/// </summary>
/// <remarks>
/// <see cref="FoundgineProviderCapability.TenantIsolation"/> is declared because every tool call resolves the
/// caller's <c>SecurityExecutionContext</c> from the host at call time and never accepts identity, tenant,
/// audience or warrant values from the model.
/// </remarks>
public sealed class FoundgineAgentFrameworkProvider : IFoundgineProvider
{
    public const string ProviderId = "foundgine.maf.agent";

    public string Id => ProviderId;
    public string Name => "Microsoft Agent Framework agent adapter";
    public string Version => "1.0.0";
    public FoundgineProviderKind Kind => FoundgineProviderKind.Agent;
    public FoundgineProviderCapability Capabilities => FoundgineProviderCapability.TenantIsolation;
}
