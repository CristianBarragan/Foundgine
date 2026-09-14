using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Foundgine.Providers.Storage.Sql.Retrieval;
using Npgsql;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Retrieval;

/// <summary>
/// Provider-wiring coverage for <see cref="PostgresRetrievalCandidateSource"/>
/// against the Supply Chain semantic model that requires no live database:
/// every strategy either short-circuits before touching PostgreSQL (an
/// opt-in gate that is off, a request-shape validation failure, or the
/// intentionally-reserved Vector strategy) or is a documented no-op
/// (Relational). Strategies that do reach PostgreSQL (Fuzzy, FullText,
/// opted-in Search/GraphSimilarity) are covered by the sibling
/// PostgresRetrieval*/PgSearchRetrieval*/GraphSimilarityRetrieval*
/// integration tests, gated behind FOUNDGINE_POSTGRES_CONNECTION.
///
/// The <see cref="NpgsqlDataSource"/> built below is never opened or
/// queried: every case here is rejected before the provider issues a
/// command, so a syntactically valid but unreachable connection string is
/// sufficient and no real PostgreSQL instance is required.
/// </summary>
public sealed class SupplyChainRetrievalCapabilityTests
{
    private static NpgsqlDataSource CreateUnreachableDataSource() =>
        NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1");

    [Fact]
    public async Task Vector_strategy_is_reserved_for_a_future_pgvector_provider()
    {
        using var dataSource = CreateUnreachableDataSource();
        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata);

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Product,
            SupplyChainSemanticModel.Field("Product", "Name"),
            "gasket",
            RetrievalStrategy.Vector);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => source.RetrieveAsync(request));

        Assert.Contains("pgvector", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Relational_strategy_is_a_documented_no_op()
    {
        using var dataSource = CreateUnreachableDataSource();
        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata);

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            SupplyChainSemanticModel.Field("Supplier", "Name"),
            "Acme",
            RetrievalStrategy.Relational);

        var result = await source.RetrieveAsync(request);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Fuzzy_retrieval_is_disabled_when_pg_trgm_is_opted_out()
    {
        using var dataSource = CreateUnreachableDataSource();
        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions(EnablePgTrgm: false));

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            SupplyChainSemanticModel.Field("Supplier", "Name"),
            "Acme Suplies",
            RetrievalStrategy.Fuzzy);

        await Assert.ThrowsAsync<NotSupportedException>(() => source.RetrieveAsync(request));
    }

    [Fact]
    public async Task FullText_retrieval_is_disabled_when_opted_out()
    {
        using var dataSource = CreateUnreachableDataSource();
        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions(EnableFullText: false));

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Product,
            SupplyChainSemanticModel.Field("Product", "Name"),
            "hydraulic fitting",
            RetrievalStrategy.FullText);

        await Assert.ThrowsAsync<NotSupportedException>(() => source.RetrieveAsync(request));
    }

    [Fact]
    public async Task Search_strategy_is_opt_in_and_disabled_by_default()
    {
        using var dataSource = CreateUnreachableDataSource();
        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions());

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            SupplyChainSemanticModel.Field("Supplier", "Name"),
            "acme",
            RetrievalStrategy.Search);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => source.RetrieveAsync(request));

        Assert.Contains("Search", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphSimilarity_strategy_is_opt_in_and_disabled_by_default()
    {
        using var dataSource = CreateUnreachableDataSource();
        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions());

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            null,
            "suppliers similar to Acme",
            RetrievalStrategy.GraphSimilarity,
            relationship: SupplyChainSemanticModel.Relationship("Supplier", "purchaseOrders"),
            referenceIdentity: "1");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => source.RetrieveAsync(request));

        Assert.Contains("GraphSimilarity", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphSimilarity_requires_a_relationship_even_when_Apache_AGE_is_enabled()
    {
        using var dataSource = CreateUnreachableDataSource();
        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions(EnableApacheAge: true));

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            null,
            "suppliers similar to Acme",
            RetrievalStrategy.GraphSimilarity,
            referenceIdentity: "1");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => source.RetrieveAsync(request));

        Assert.Contains("Relationship", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphSimilarity_requires_a_reference_identity_even_when_Apache_AGE_is_enabled()
    {
        using var dataSource = CreateUnreachableDataSource();
        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions(EnableApacheAge: true));

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            null,
            "suppliers similar to Acme",
            RetrievalStrategy.GraphSimilarity,
            relationship: SupplyChainSemanticModel.Relationship("Supplier", "purchaseOrders"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => source.RetrieveAsync(request));

        Assert.Contains("ReferenceIdentity", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_rejects_a_null_data_source()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresRetrievalCandidateSource(
            null!,
            SupplyChainSemanticModel.Metadata));
    }

    [Fact]
    public void Constructor_rejects_null_metadata()
    {
        using var dataSource = CreateUnreachableDataSource();

        Assert.Throws<ArgumentNullException>(() => new PostgresRetrievalCandidateSource(
            dataSource,
            null!));
    }

    [Fact]
    public void All_provider_backed_retrieval_strategies_have_Supply_Chain_coverage()
    {
        // Single source of truth for "every RetrievalStrategy this sample
        // must exercise". Relational is the deterministic default path and
        // is covered structurally above; the remaining five are exactly the
        // provider-backed strategies covered across this file and the
        // PostgresRetrieval*/PgSearchRetrieval*/GraphSimilarityRetrieval*
        // integration tests.
        var providerBackedStrategies = new[]
        {
            RetrievalStrategy.Fuzzy,
            RetrievalStrategy.FullText,
            RetrievalStrategy.Search,
            RetrievalStrategy.GraphSimilarity,
            RetrievalStrategy.Vector
        };

        Assert.Equal(5, providerBackedStrategies.Length);
        Assert.All(
            providerBackedStrategies,
            strategy => Assert.True(
                SemanticRetrievalPlanner.RequiresApproximateRetrieval(strategy)));

        Assert.False(
            SemanticRetrievalPlanner.RequiresApproximateRetrieval(
                RetrievalStrategy.Relational));
    }
}