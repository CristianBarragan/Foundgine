using Microsoft.Extensions.DependencyInjection;

namespace Foundgine.Runtime;

/// <summary>
/// A named, self-contained unit of Foundgine configuration that can be switched on or off with
/// <see cref="FoundgineOptions.Enable{T}"/> / <see cref="FoundgineOptions.Disable{T}"/> instead of a
/// bespoke <c>DependencyInjection.cs</c> file per layer. Built-in framework capabilities live in
/// <see cref="Foundgine.Runtime.Capabilities"/> (and, for provider-specific ones, in the matching
/// namespace of the provider package); application capabilities implement the same interface, so
/// <c>options.Enable&lt;SupplyChainDomain&gt;()</c> reads the same way as
/// <c>options.Enable&lt;HighAssurance&gt;()</c>.
/// </summary>
public interface IFoundgineCapability
{
    /// <summary>
    /// Registers whatever services and <see cref="FoundgineOptions"/> configuration this capability
    /// contributes. Invoked once per enabled capability, in the order <see cref="FoundgineOptions.Enable{T}"/>
    /// was called, immediately after the <c>configure</c> delegate passed to <c>AddFoundgine</c> returns
    /// and before <see cref="FoundgineOptions.Model"/> / <see cref="FoundgineOptions.AuthorizationPolicy"/>
    /// are resolved - so a capability may supply either (or both) of those in addition to registering
    /// services via <see cref="FoundgineCapabilityContext.Services"/>.
    /// </summary>
    static abstract void Configure(FoundgineCapabilityContext context);
}

/// <summary>Everything an <see cref="IFoundgineCapability"/> needs in order to configure itself.</summary>
public sealed class FoundgineCapabilityContext
{
    /// <summary>The options object being built by the enclosing <c>AddFoundgine</c> call.</summary>
    public FoundgineOptions Options { get; }

    /// <summary>The service collection the host is registering into.</summary>
    public IServiceCollection Services { get; }

    public FoundgineCapabilityContext(FoundgineOptions options, IServiceCollection services)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }
}
