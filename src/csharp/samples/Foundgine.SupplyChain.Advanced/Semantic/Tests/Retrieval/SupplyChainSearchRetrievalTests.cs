using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Foundgine.Providers.Storage.Sql.Retrieval;
using Npgsql;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Retrieval;

/// <summary>
/// Exercises <see cref="PostgresRetrievalCandidateSource"/>'s
/// <see cref="RetrievalStrategy.Search"/> strategy, which is deliberately
/// isolated to the pg_search (ParadeDB BM25) extension per
/// src/csharp/Foundgine.Providers.Storage.Sql/README.md. pg_search is not installed on a vanilla
/// PostgreSQL image, so - unlike Fuzzy/FullText - this requires an explicit
/// second opt-in on top of the connection string: FOUNDGINE_POSTGRES_PGSEARCH=1.
/// </summary>
public sealed class SupplyChainSearchRetrievalTests
{
    [PgSearchFact]
    public async Task Search_retrieval_ranks_suppliers_by_bm25_relevance()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions(EnablePgSearch: true));

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            SupplyChainSemanticModel.Field("Supplier", "Name"),
            "metal fabrication",
            RetrievalStrategy.Search,
            limit: 5);

        var result = await source.RetrieveAsync(request);

        Assert.NotEmpty(result);
        Assert.All(result, c => Assert.Equal(CandidateEvidenceKind.Bm25, c.EvidenceKind));

        for (var i = 1; i < result.Count; i++)
            Assert.True(result[i - 1].Score >= result[i].Score);
    }

    [PgSearchFact]
    public async Task Search_strategy_still_throws_when_not_opted_in_on_a_pg_search_capable_instance()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata,
            new PostgresRetrievalOptions(EnablePgSearch: false));

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            SupplyChainSemanticModel.Field("Supplier", "Name"),
            "metal fabrication",
            RetrievalStrategy.Search);

        await Assert.ThrowsAsync<NotSupportedException>(() => source.RetrieveAsync(request));
    }

    private static async Task SeedAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        const string ddl =
            """
            CREATE EXTENSION IF NOT EXISTS pg_search;

            DROP TABLE IF EXISTS "suppliers";
            CREATE TABLE "suppliers" (
                "Id" integer PRIMARY KEY,
                "Name" text NOT NULL,
                "Country" text NOT NULL,
                "RiskScore" numeric NOT NULL,
                "TenantId" text NOT NULL
            );
            """;

        await using (var command = new NpgsqlCommand(ddl, connection))
            await command.ExecuteNonQueryAsync();

        const string seed =
            """
            INSERT INTO "suppliers" ("Id", "Name", "Country", "RiskScore", "TenantId") VALUES
                (1, 'Acme Supplies', 'US', 0.20, 'tenant-a'),
                (2, 'Best Metal Fabrication Works', 'DE', 0.35, 'tenant-a'),
                (3, 'Continental Metal Fabricators', 'DE', 0.42, 'tenant-a'),
                (4, 'Northwind Traders', 'US', 0.15, 'tenant-a');
            """;

        await using (var command = new NpgsqlCommand(seed, connection))
            await command.ExecuteNonQueryAsync();

        const string index =
            """
            DROP INDEX IF EXISTS "suppliers_bm25_idx";
            CREATE INDEX "suppliers_bm25_idx" ON "suppliers"
                USING bm25 ("Id", "Name")
                WITH (key_field = 'Id');
            """;

        await using (var command = new NpgsqlCommand(index, connection))
            await command.ExecuteNonQueryAsync();
    }
}