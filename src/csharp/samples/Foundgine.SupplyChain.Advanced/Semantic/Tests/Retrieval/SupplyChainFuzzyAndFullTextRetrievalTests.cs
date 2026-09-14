using Foundgine.Core.Semantic.Resolution;
using Foundgine.SupplyChain.Advanced.Semantics;
using Foundgine.Providers.Storage.Sql.Retrieval;
using Npgsql;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Retrieval;

/// <summary>
/// Exercises <see cref="PostgresRetrievalCandidateSource"/> against a real
/// PostgreSQL instance for the two providers that are enabled by default:
/// pg_trgm-backed <see cref="RetrievalStrategy.Fuzzy"/> and native
/// <see cref="RetrievalStrategy.FullText"/> search, seeded with data shaped
/// like the Supply Chain sample's own Supplier/Product tables.
///
/// Opt in with FOUNDGINE_POSTGRES_CONNECTION (or
/// FOUNDGINE_POSTGRES_CONNECTION_STRING); otherwise every test here is
/// skipped rather than failed.
/// </summary>
public sealed class SupplyChainFuzzyAndFullTextRetrievalTests
{
    [PostgresRetrievalFact]
    public async Task Fuzzy_retrieval_matches_a_misspelled_supplier_name()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata);

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            SupplyChainSemanticModel.Field("Supplier", "Name"),
            "Acme Suplies",
            RetrievalStrategy.Fuzzy,
            limit: 5);

        var result = await source.RetrieveAsync(request);

        Assert.Contains(result, c => c.RecordId == "1");
        Assert.All(result, c => Assert.Equal(CandidateEvidenceKind.Trigram, c.EvidenceKind));
        Assert.All(result, c => Assert.Single(c.EffectiveEvidence));
    }

    [PostgresRetrievalFact]
    public async Task Fuzzy_retrieval_respects_the_requested_limit()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata);

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            SupplyChainSemanticModel.Field("Supplier", "Name"),
            "Metal",
            RetrievalStrategy.Fuzzy,
            limit: 1);

        var result = await source.RetrieveAsync(request);

        Assert.True(result.Count <= 1);
    }

    [PostgresRetrievalFact]
    public async Task Fuzzy_retrieval_returns_no_candidates_for_an_unrelated_query()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata);

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Supplier,
            SupplyChainSemanticModel.Field("Supplier", "Name"),
            "zzzzz-completely-unrelated-zzzzz",
            RetrievalStrategy.Fuzzy);

        var result = await source.RetrieveAsync(request);

        Assert.Empty(result);
    }

    [PostgresRetrievalFact]
    public async Task FullText_retrieval_matches_a_product_by_natural_language_query()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata);

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Product,
            SupplyChainSemanticModel.Field("Product", "Name"),
            "hydraulic fitting",
            RetrievalStrategy.FullText,
            limit: 5);

        var result = await source.RetrieveAsync(request);

        Assert.Contains(result, c => c.RecordId == "1");
        Assert.All(result, c => Assert.Equal(CandidateEvidenceKind.FullText, c.EvidenceKind));
    }

    [PostgresRetrievalFact]
    public async Task FullText_retrieval_ranks_stronger_lexical_matches_first()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            PostgresRetrievalTestEnvironment.ConnectionString);
        await SeedAsync(dataSource);

        var source = new PostgresRetrievalCandidateSource(
            dataSource,
            SupplyChainSemanticModel.Metadata);

        var request = new SemanticRetrievalRequest(
            SupplyChainSemanticModel.Product,
            SupplyChainSemanticModel.Field("Product", "Name"),
            "steel gasket",
            RetrievalStrategy.FullText,
            limit: 10);

        var result = await source.RetrieveAsync(request);

        Assert.NotEmpty(result);
        for (var i = 1; i < result.Count; i++)
            Assert.True(result[i - 1].Score >= result[i].Score);
    }

    /// <summary>
    /// Recreates a minimal slice of the Supply Chain schema (only the columns
    /// exercised by approximate retrieval) with the exact storage names the
    /// generated metadata expects: table names come from each entity's
    /// <c>StorageName</c>, and column names default to the CLR property name
    /// when no explicit override is declared - see
    /// <c>src/csharp/samples/Foundgine.SupplyChain.Advanced/Semantic/Domain/Domain.cs</c>.
    /// </summary>
    private static async Task SeedAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        const string ddl =
            """
            CREATE EXTENSION IF NOT EXISTS pg_trgm;

            DROP TABLE IF EXISTS "suppliers";
            CREATE TABLE "suppliers" (
                "Id" integer PRIMARY KEY,
                "Name" text NOT NULL,
                "Country" text NOT NULL,
                "RiskScore" numeric NOT NULL,
                "TenantId" text NOT NULL
            );
            CREATE INDEX ON "suppliers" USING gin ("Name" gin_trgm_ops);

            DROP TABLE IF EXISTS "products";
            CREATE TABLE "products" (
                "Id" integer PRIMARY KEY,
                "Sku" text NOT NULL,
                "Name" text NOT NULL,
                "Category" text NOT NULL,
                "SafetyStock" numeric NOT NULL
            );
            """;

        await using (var command = new NpgsqlCommand(ddl, connection))
            await command.ExecuteNonQueryAsync();

        const string seed =
            """
            INSERT INTO "suppliers" ("Id", "Name", "Country", "RiskScore", "TenantId") VALUES
                (1, 'Acme Supplies', 'US', 0.20, 'tenant-a'),
                (2, 'Best Metal Works', 'DE', 0.35, 'tenant-a'),
                (3, 'Continental Metal Fabricators', 'DE', 0.42, 'tenant-a'),
                (4, 'Northwind Traders', 'US', 0.15, 'tenant-a');

            INSERT INTO "products" ("Id", "Sku", "Name", "Category", "SafetyStock") VALUES
                (1, 'HYD-100', 'Steel hydraulic fitting, 1 inch', 'Fittings', 50),
                (2, 'HYD-200', 'Brass hydraulic fitting, half inch', 'Fittings', 75),
                (3, 'GSK-010', 'Steel gasket, high pressure', 'Seals', 120),
                (4, 'BOX-001', 'Corrugated shipping box, large', 'Packaging', 200);
            """;

        await using (var command = new NpgsqlCommand(seed, connection))
            await command.ExecuteNonQueryAsync();
    }
}