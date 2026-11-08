using Foundgine.Providers.Gateway.Providers;

namespace Foundgine.Extensions.Temporal.Hosting;

/// <summary>Declares Temporal as the Foundgine Workflow provider so startup validation and discovery can see it.</summary>
public sealed class FoundgineTemporalProvider : IFoundgineProvider
{
    public string Id => "foundgine.temporal.workflow";
    public string Name => "Temporal workflow engine";
    public string Version => "1.0.0";
    public FoundgineProviderKind Kind => FoundgineProviderKind.Workflow;

    public FoundgineProviderCapability Capabilities =>
        FoundgineProviderCapability.DurableExecution |
        FoundgineProviderCapability.WorkflowSignals |
        FoundgineProviderCapability.WorkflowQueries |
        FoundgineProviderCapability.Retry |
        FoundgineProviderCapability.TenantIsolation;
}
