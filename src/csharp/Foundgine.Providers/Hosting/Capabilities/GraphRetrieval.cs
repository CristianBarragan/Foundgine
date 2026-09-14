using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Providers.Storage.Sql.Retrieval;
using Foundgine.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Foundgine.Providers.Capabilities;

/// <summary>
/// Enables graph-similarity candidate retrieval (Apache AGE, via <see cref="PostgresRetrievalOptions.EnableApacheAge"/>)
/// over the metadata-projected semantic graph, in addition to the trigram/full-text strategies
/// <see cref="PostgresRetrievalCandidateSource"/> already supports. Registers
/// <see cref="PostgresRetrievalCandidateSource"/> as <see cref="IApproximateCandidateSource"/>,
/// resolving <see cref="NpgsqlDataSource"/> and <see cref="IMetadataCatalog"/> from the container -
/// both must already be registered by some other capability or call (typically the application's own
/// domain capability, which owns the connection string and the AOT-generated metadata registry).
/// </summary>
public sealed class GraphRetrieval : IFoundgineCapability
{
    public static void Configure(FoundgineCapabilityContext context) =>
        context.Services.AddSingleton<IApproximateCandidateSource>(sp =>
            new PostgresRetrievalCandidateSource(
                sp.GetRequiredService<NpgsqlDataSource>(),
                sp.GetRequiredService<IMetadataCatalog>(),
                new PostgresRetrievalOptions(EnableApacheAge: true)));
}

/// <summary>Fluent <c>Use</c>/<c>Disable</c> surface for <see cref="GraphRetrieval"/>.</summary>
public static class GraphRetrievalFoundgineOptionsExtensions
{
    /// <summary>Enables <see cref="GraphRetrieval"/>. Equivalent to <c>options.Enable&lt;GraphRetrieval&gt;()</c>.</summary>
    public static FoundgineOptions UseGraphRetrieval(this FoundgineOptions options) =>
        options.Enable<GraphRetrieval>();

    public static FoundgineOptions DisableGraphRetrieval(this FoundgineOptions options) =>
        options.Disable<GraphRetrieval>();
}
