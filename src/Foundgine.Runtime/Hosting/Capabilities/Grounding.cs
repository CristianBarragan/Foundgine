using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Foundgine.Runtime.Capabilities;

/// <summary>
/// Optional capability that wires <see cref="SemanticLexicalResolver"/> so free-form lexical expressions can be resolved
/// against the frozen semantic graph. Skip (disable) this for hosts, such as a typed MCP tool
/// surface, whose callers always supply already-identified entities (a customer id, an order id, ...)
/// and never a free-text query - there is nothing for a lexical resolver to ground.
///
/// Enabling this capability does not by itself supply a candidate source: it registers
/// <see cref="SemanticLexicalResolver"/> resolving <see cref="ISemanticLexicalCandidateSource"/> from
/// the container, which some other registration (a provider package, e.g. the pgvector or
/// Elasticsearch candidate sources in <c>Foundgine.Providers</c>) must supply separately. Resolving
/// <see cref="SemanticLexicalResolver"/> without one throws at first use, not at startup, since
/// candidate sources commonly register themselves independently of capability enable order.
/// </summary>
public sealed class Grounding : IFoundgineCapability
{
    public static void Configure(FoundgineCapabilityContext context)
    {
        context.Services.AddSingleton(sp => new SemanticLexicalResolver(
            sp.GetRequiredService<SemanticContractSnapshot>(),
            sp.GetRequiredService<ISemanticLexicalCandidateSource>()));

        context.Services.AddSingleton(sp => new SemanticLexicalReadIntentGrounder(
            sp.GetRequiredService<SemanticContractSnapshot>(),
            sp.GetRequiredService<SemanticLexicalResolver>()));
    }
}

/// <summary>Fluent <c>Use</c>/<c>Disable</c> surface for <see cref="Grounding"/>.</summary>
public static class GroundingFoundgineOptionsExtensions
{
    /// <summary>Enables <see cref="Grounding"/>. Equivalent to <c>options.Enable&lt;Grounding&gt;()</c>.</summary>
    public static FoundgineOptions UseGrounding(this FoundgineOptions options) =>
        options.Enable<Grounding>();

    /// <summary>Disables <see cref="Grounding"/>. Equivalent to <c>options.Disable&lt;Grounding&gt;()</c>.</summary>
    public static FoundgineOptions DisableGrounding(this FoundgineOptions options) =>
        options.Disable<Grounding>();
}