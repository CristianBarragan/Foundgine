using Foundgine.Providers.Storage.PostgresVector;
using Npgsql;
using Xunit;

namespace Foundgine.Postgres.Vector.Tests;

public sealed class PgVectorSemanticLexicalCandidateSourceTests
{
    [Theory]
    [InlineData(PgVectorDistance.Cosine, "<=>")]
    [InlineData(PgVectorDistance.L2, "<->")]
    [InlineData(PgVectorDistance.InnerProduct, "<#>")]
    public void DistanceOperator_maps_each_distance_to_its_pgvector_operator(
        PgVectorDistance distance, string expectedOperator)
    {
        Assert.Equal(expectedOperator, PgVectorSemanticLexicalCandidateSource.DistanceOperator(distance));
    }

    [Fact]
    public void DistanceOperator_rejects_an_undefined_distance_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PgVectorSemanticLexicalCandidateSource.DistanceOperator((PgVectorDistance)99));
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.25, 0.75)]
    public void ToScore_for_cosine_distance_is_one_minus_distance(double distance, double expectedScore)
    {
        var score = PgVectorSemanticLexicalCandidateSource.ToScore(distance, PgVectorDistance.Cosine);

        Assert.Equal(expectedScore, score, precision: 10);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.5)]
    [InlineData(3.0, 0.25)]
    public void ToScore_for_l2_distance_folds_the_unbounded_distance_into_zero_to_one(
        double distance, double expectedScore)
    {
        var score = PgVectorSemanticLexicalCandidateSource.ToScore(distance, PgVectorDistance.L2);

        Assert.Equal(expectedScore, score, precision: 10);
        Assert.InRange(score, 0d, 1d);
    }

    [Theory]
    [InlineData(-0.9, 0.9)]
    [InlineData(0.4, -0.4)]
    public void ToScore_for_inner_product_negates_the_stored_negative_inner_product(
        double distance, double expectedScore)
    {
        var score = PgVectorSemanticLexicalCandidateSource.ToScore(distance, PgVectorDistance.InnerProduct);

        Assert.Equal(expectedScore, score, precision: 10);
    }

    [Fact]
    public void ToScore_rejects_an_undefined_distance_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PgVectorSemanticLexicalCandidateSource.ToScore(0.1, (PgVectorDistance)99));
    }

    [Fact]
    public void Constructor_throws_when_data_source_is_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PgVectorSemanticLexicalCandidateSource(null!, new StubEmbeddingGenerator()));
    }

    [Fact]
    public void Constructor_throws_when_embedding_generator_is_null()
    {
        // A lazily-built data source never opens a physical connection, so this
        // exercises the guard clause without requiring a live PostgreSQL server.
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Database=unused").Build();

        Assert.Throws<ArgumentNullException>(() => new PgVectorSemanticLexicalCandidateSource(dataSource, null!));
    }

    [Fact]
    public void Constructor_defaults_options_when_none_are_supplied()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Database=unused").Build();

        // Should not throw: null options fall back to PgVectorOptions defaults.
        _ = new PgVectorSemanticLexicalCandidateSource(dataSource, new StubEmbeddingGenerator(), options: null);
    }

    [Fact]
    public async Task Retrieve_throws_when_request_is_null()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Database=unused").Build();
        var source = new PgVectorSemanticLexicalCandidateSource(dataSource, new StubEmbeddingGenerator());

        Assert.Throws<ArgumentNullException>(() => source.Retrieve(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => source.RetrieveAsync(null!));
    }

    private sealed class StubEmbeddingGenerator : Foundgine.Core.Semantic.Resolution.ISemanticEmbeddingGenerator
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new float[] { 1f });

        public Task<IReadOnlyList<float[]>> EmbedManyAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 1f }).ToArray());
    }
}