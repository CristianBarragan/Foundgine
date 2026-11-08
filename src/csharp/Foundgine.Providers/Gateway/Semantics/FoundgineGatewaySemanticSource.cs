using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Runtime;

namespace Foundgine.Providers.Gateway.Semantics;

/// <summary>
/// Read-only bridge from the gateway to Foundgine's canonical semantic capability contract.
/// This deliberately delegates discovery to IFoundgine; it does not rebuild metadata or
/// introduce a second authorization/discovery implementation.
/// </summary>
public interface IFoundgineGatewaySemanticSource
{
    FoundgineGatewaySemanticDescription Describe();
}

/// <summary>Stable response containing the native semantic contract and compatibility versions.</summary>
public sealed record FoundgineGatewaySemanticDescription(
    SemanticCapabilityContract Contract,
    SemanticVersionSet Versions);

/// <summary>Adapts the existing IFoundgine application surface for gateway consumers.</summary>
public sealed class FoundgineGatewaySemanticSource : IFoundgineGatewaySemanticSource
{
    private readonly IFoundgine _foundgine;

    public FoundgineGatewaySemanticSource(IFoundgine foundgine)
    {
        _foundgine = foundgine ?? throw new ArgumentNullException(nameof(foundgine));
    }

    /// <remarks>
    /// The host must authenticate and authorize access to this discovery endpoint.
    /// The returned contract is descriptive only; every execution still goes through
    /// IFoundgine and its execution-time authorization checks.
    /// </remarks>
    public FoundgineGatewaySemanticDescription Describe() => new(
        _foundgine.DescribeCapabilityContract(),
        _foundgine.DescribeVersionSet());
}
