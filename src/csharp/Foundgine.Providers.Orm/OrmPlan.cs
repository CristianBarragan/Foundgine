using Foundgine.Core.Execution;

namespace Foundgine.Providers.Orm;

/// <summary>
/// Provider plan for ORM execution. It carries the provider-neutral IR and the exact capability
/// contract used to compile it; provider expressions are composed per execution so authorization
/// context values are never captured in a cached plan.
/// </summary>
public sealed record OrmPlan(string ProviderName, ExecutionIR IR) : ProviderPlan(ProviderName)
{
    /// <summary>Version of the capability contract under which this plan was validated.</summary>
    public string CapabilityContractVersion { get; init; } = "unbound";

    /// <summary>Feature declaration used during compilation; this is not security evidence.</summary>
    public OrmProviderFeature DeclaredFeatures { get; init; } = OrmProviderFeature.None;
}
