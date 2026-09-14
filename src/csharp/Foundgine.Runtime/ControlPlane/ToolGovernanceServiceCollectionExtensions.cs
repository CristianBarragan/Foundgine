using Foundgine.Runtime.ControlPlane.Approvals;
using Foundgine.Runtime.ControlPlane.AuditLog;
using Foundgine.Runtime.ControlPlane.PolicyGateway;
using Foundgine.Runtime.ControlPlane.RiskScoring;
using Foundgine.Runtime.ControlPlane.ToolRegistry;
using Foundgine.Runtime.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Foundgine.Runtime.ControlPlane;

/// <summary>
/// DI registration for the tool-call governance control plane (routing,
/// tool registry, risk scoring, policy gateway, approvals, audit log, and
/// the <see cref="ToolCallGovernor"/> that composes them). Registers the
/// in-memory implementations by default; a production host can override any
/// individual registration (e.g. <see cref="IApprovalStore"/>,
/// <see cref="IAuditLog"/>) with a durable implementation before or after
/// calling this method — the last registration wins.
/// </summary>
public static class ToolGovernanceServiceCollectionExtensions
{
    public static IServiceCollection AddFoundgineToolGovernance(
        this IServiceCollection services,
        Action<ToolGovernanceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IToolRegistry, InMemoryToolRegistry>();
        services.AddSingleton<IApprovalStore, InMemoryApprovalStore>();
        services.AddSingleton<IAuditLog, InMemoryAuditLog>();
        services.AddSingleton<CompositeRiskScorer>();
        services.AddSingleton<IPolicyGateway, DefaultPolicyGateway>();
        services.AddSingleton<IRoutingEngine, DefaultRoutingEngine>();
        services.AddSingleton<ToolCallGovernor>();

        configure?.Invoke(new ToolGovernanceBuilder(services));

        return services;
    }
}

/// <summary>Fluent surface for registering rules against the governance pipeline registered by <see cref="ToolGovernanceServiceCollectionExtensions.AddFoundgineToolGovernance"/>.</summary>
public sealed class ToolGovernanceBuilder
{
    private readonly IServiceCollection _services;

    internal ToolGovernanceBuilder(IServiceCollection services) => _services = services;

    public ToolGovernanceBuilder AddRiskRule<TRule>() where TRule : class, IRiskRule
    {
        _services.AddSingleton<IRiskRule, TRule>();
        return this;
    }

    public ToolGovernanceBuilder AddPolicyRule<TRule>() where TRule : class, IPolicyRule
    {
        _services.AddSingleton<IPolicyRule, TRule>();
        return this;
    }

    public ToolGovernanceBuilder AddRoutingRule<TRule>() where TRule : class, IRoutingRule
    {
        _services.AddSingleton<IRoutingRule, TRule>();
        return this;
    }

    /// <summary>
    /// Seeds the tool registry with a descriptor at startup. Collected via
    /// DI into <see cref="InMemoryToolRegistry"/>'s constructor, so multiple
    /// calls accumulate rather than overwrite.
    /// </summary>
    public ToolGovernanceBuilder RegisterTool(ToolDescriptor descriptor)
    {
        _services.AddSingleton(descriptor);
        return this;
    }
}