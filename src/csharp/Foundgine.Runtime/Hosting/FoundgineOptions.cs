using Foundgine.Core.Semantic;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Security.Warrants;
using Foundgine.Core.Semantic.Security.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Foundgine.Runtime;

/// <summary>
/// Application-level Foundgine configuration. Provider-specific services
/// remain outside this object and are registered by the provider adapter.
/// </summary>
public sealed class FoundgineOptions
{
    /// <summary>
    /// Optional structural metadata source. When supplied, Foundgine discovers
    /// the ordinary semantic model from metadata before applying semantic
    /// configuration.
    /// </summary>
    public IMetadataCatalog? Metadata { get; private set; }

    /// <summary>Optional semantic enrichment applied after structural discovery.</summary>
    public Action<SemanticModelBuilder>? SemanticConfiguration { get; private set; }

    /// <summary>Optional application authorization configuration.</summary>
    public SemanticAuthorizationConfiguration? AuthorizationConfiguration { get; private set; }

    /// <summary>Execution identity consumed by configured authorization rules.</summary>
    public SemanticAuthorizationContext AuthorizationContext { get; private set; } = new();

    public SemanticModel? Model { get; set; }

    public ISemanticAuthorizationPolicy? AuthorizationPolicy { get; set; }

    public FoundgineOptions UseMetadata(IMetadataCatalog metadata)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        return this;
    }

    public FoundgineOptions ConfigureSemantics(Action<SemanticModelBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        SemanticConfiguration = SemanticConfiguration is null
            ? configure
            : Compose(SemanticConfiguration, configure);
        return this;
    }

    public FoundgineOptions ConfigureAuthorization(
        Action<SemanticAuthorizationConfiguration> configure,
        SemanticAuthorizationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AuthorizationConfiguration ??= new SemanticAuthorizationConfiguration();
        configure(AuthorizationConfiguration);
        if (context is not null)
            AuthorizationContext = context;
        return this;
    }

    private static Action<SemanticModelBuilder> Compose(
        Action<SemanticModelBuilder> first,
        Action<SemanticModelBuilder> second) => builder =>
    {
        first(builder);
        second(builder);
    };

    /// <summary>
    /// Optional cache for compiled provider plans. Authorization is always evaluated
    /// before a cache lookup, and authorization predicates remain part of the cached plan.
    /// </summary>
    public IProviderPlanCache? PlanCache { get; set; }

    /// <summary>Optional trusted key resolver for signed semantic security warrants.</summary>
    public ISecurityWarrantKeyResolver? WarrantKeyResolver { get; set; }

    /// <summary>Optional trusted issuer expected on incoming warrants.</summary>
    public string? ExpectedWarrantIssuer { get; set; }

    /// <summary>Optional replay store used when executing warrant-backed requests.</summary>
    public ISecurityWarrantReplayStore? WarrantReplayStore { get; set; }

    /// <summary>Canonical engine-side bounds for untrusted semantic request complexity.</summary>
    public SecurityResourceLimits SecurityResourceLimits { get; set; } = new();

    /// <summary>Optional trusted authority consulted immediately before provider execution.</summary>
    public IExecutionAuthorizationRevalidator? ExecutionAuthorizationRevalidator { get; set; }

    /// <summary>Optional current authorization authority state resolver used for execution-time revalidation.</summary>
    public Func<SemanticAuthorizationEvidence, CancellationToken, ValueTask<ExecutionAuthorizationAuthorityState?>>?
        ExecutionAuthorizationAuthorityResolver { get; set; }

    /// <summary>Optional mutation schema and provider for the semantic mutation pipeline.</summary>
    public IMutationSchema? MutationSchema { get; set; }

    public Foundgine.Core.Execution.Mutation.IMutationBatchExecutionProvider? MutationProvider { get; set; }

    private readonly List<Type> _enabledCapabilityOrder = [];
    private readonly Dictionary<Type, Action<FoundgineCapabilityContext>> _enabledCapabilities = [];

    /// <summary>
    /// The capability marker types currently enabled, in <see cref="Enable{T}"/> call order.
    /// No optional capabilities are enabled by default; applications opt into them explicitly.
    /// </summary>
    public IReadOnlyList<Type> EnabledCapabilities => _enabledCapabilityOrder;

    /// <summary>
    /// Enables an optional capability: <c>T.Configure</c> runs once, during <c>AddFoundgine</c>, after this
    /// <c>configure</c> delegate returns. Calling <c>Enable&lt;T&gt;()</c> again for an already-enabled
    /// T moves it to the end of the application order but does not run <c>Configure</c> twice.
    /// </summary>
    public FoundgineOptions Enable<T>() where T : IFoundgineCapability
    {
        var type = typeof(T);
        _enabledCapabilityOrder.Remove(type);
        _enabledCapabilityOrder.Add(type);
        _enabledCapabilities[type] = T.Configure;
        return this;
    }

    /// <summary>
    /// Disables a previously (or default-) enabled capability. A no-op if it was never enabled.
    /// </summary>
    public FoundgineOptions Disable<T>() where T : IFoundgineCapability
    {
        var type = typeof(T);
        _enabledCapabilityOrder.Remove(type);
        _enabledCapabilities.Remove(type);
        return this;
    }

    /// <summary>Whether <typeparamref name="T"/> is currently enabled.</summary>
    public bool IsEnabled<T>() where T : IFoundgineCapability => _enabledCapabilities.ContainsKey(typeof(T));

    /// <summary>
    /// Runs every enabled capability's <c>Configure</c>, in <see cref="Enable{T}"/> order. Called by
    /// <see cref="FoundgineServiceCollectionExtensions.AddFoundgine(IServiceCollection, Action{FoundgineOptions})"/>
    /// right after the host's <c>configure</c> delegate returns.
    /// </summary>
    internal void ApplyCapabilities(IServiceCollection services)
    {
        var context = new FoundgineCapabilityContext(this, services);
        foreach (var type in _enabledCapabilityOrder)
            _enabledCapabilities[type](context);
    }
}