using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Foundgine.Providers.Storage.Sql.Retrieval;
using Npgsql;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Retrieval;

/// <summary>
/// Exercises <see cref="PostgresRetrievalCandidateSource"/>'s
/// <see cref="RetrievalStrategy.GraphSimilarity"/> strategy, backed by
/// Apache AGE per src/csharp/Foundgine.Providers.Storage.Sql/README.md. AGE is not installed on a
/// vanilla PostgreSQL image, so - unlike Fuzzy/FullText - this requires an
/// explicit second opt-in on top of the connection string:
/// FOUNDGINE_POSTGRES_AGE=1.
///
/// The graph is modeled on the Supply Chain sample's own
/// <c>Supplier.purchaseOrders</c> relationship: two suppliers are
/// "neighbor-similar" when they both connect to the same purchase-order
/// vertex, the same shape a real risk/co-sourcing similarity graph would use
/// even though a single physical purchase order has exactly one owning
/// supplier - the AGE graph is a separate, purpose-built retrieval index,
/// not a mirror of the relational foreign key.
/// </summary>
public sealed class SupplyChainGraphSimilarityRetrievalTests
{
    [ApacheAgeFact]
    public async Task GraphSimilarity_retrieval_finds_suppliers_sharing_a_purchase_order_neighbor()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedGraphAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions(EnableApacheAge: true));

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            null,
            "suppliers similar to Acme Supplies",
            RetrievalStrategy.GraphSimilarity,
            limit: 5,
            relationship: SupplyChainSemanticModel.Relationship("Supplier", "purchaseOrders"),
            referenceIdentity: "1");

        var result = await source.RetrieveAsync(request);

        Assert.Contains(result, c => c.RecordId == "2");
        Assert.DoesNotContain(result, c => c.RecordId == "1");
        Assert.All(result, c => Assert.Equal(CandidateEvidenceKind.GraphSimilarity, c.EvidenceKind));
    }

    [ApacheAgeFact]
    public async Task GraphSimilarity_retrieval_returns_no_candidates_for_an_isolated_supplier()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedGraphAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions(EnableApacheAge: true));

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            null,
            "suppliers similar to Northwind Traders",
            RetrievalStrategy.GraphSimilarity,
            relationship: SupplyChainSemanticModel.Relationship("Supplier", "purchaseOrders"),
            referenceIdentity: "4");

        var result = await source.RetrieveAsync(request);

        Assert.Empty(result);
    }

    /// <summary>
    /// Loads the AGE extension and (re)creates a small graph with vertices
    /// labeled after the "suppliers" storage name (matching
    /// <c>Supplier</c>'s effective storage name) and a "purchaseOrders" edge
    /// label (matching the semantic relationship name), which is exactly
    /// what <see cref="PostgresRetrievalCandidateSource"/> generates the
    /// Cypher query against.
    /// </summary>
    private static async Task SeedGraphAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        await using (var load = new NpgsqlCommand("LOAD 'age';", connection))
            await load.ExecuteNonQueryAsync();

        await using (var extension = new NpgsqlCommand(
                         "CREATE EXTENSION IF NOT EXISTS age;",
                         connection))
            await extension.ExecuteNonQueryAsync();

        await using (var searchPath = new NpgsqlCommand(
                         """SET search_path = ag_catalog, "$user", public;""",
                         connection))
            await searchPath.ExecuteNonQueryAsync();

        await using (var dropGraph = new NpgsqlCommand(
                         "SELECT drop_graph('foundgine', true) " +
                         "WHERE EXISTS (SELECT 1 FROM ag_graph WHERE name = 'foundgine');",
                         connection))
            await dropGraph.ExecuteNonQueryAsync();

        await using (var createGraph = new NpgsqlCommand(
                         "SELECT create_graph('foundgine');",
                         connection))
            await createGraph.ExecuteNonQueryAsync();

        const string cypher =
            """
            SELECT * FROM cypher('foundgine', $$
                CREATE (a:suppliers {id: '1'})
                CREATE (b:suppliers {id: '2'})
                CREATE (c:suppliers {id: '3'})
                CREATE (d:suppliers {id: '4'})
                CREATE (po:purchaseOrders {id: 'shared-po'})
                CREATE (a)-[:purchaseOrders]->(po)
                CREATE (b)-[:purchaseOrders]->(po)
            $$) AS (v agtype);
            """;

        await using (var seed = new NpgsqlCommand(cypher, connection))
            await seed.ExecuteNonQueryAsync();
    }
}