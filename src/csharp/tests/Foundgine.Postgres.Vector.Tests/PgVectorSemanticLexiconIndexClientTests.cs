using Foundgine.Providers.Storage.PostgresVector;
using Foundgine.Core.Semantic.Resolution;
using Npgsql;
using Xunit;

namespace Foundgine.Postgres.Vector.Tests;

public sealed class PgVectorSemanticLexiconIndexClientTests
{
    [Theory]
    [InlineData(PgVectorDistance.Cosine, "vector_cosine_ops")]
    [InlineData(PgVectorDistance.L2, "vector_l2_ops")]
    [InlineData(PgVectorDistance.InnerProduct, "vector_ip_ops")]
    public void VectorOpsClass_maps_each_distance_to_its_hnsw_operator_class(
        PgVectorDistance distance, string expectedOpsClass)
    {
        Assert.Equal(expectedOpsClass, PgVectorSemanticLexiconIndexClient.VectorOpsClass(distance));
    }

    [Fact]
    public void VectorOpsClass_rejects_an_undefined_distance_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PgVectorSemanticLexiconIndexClient.VectorOpsClass((PgVectorDistance)99));
    }

    [Fact]
    public void Constructor_throws_when_data_source_is_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PgVectorSemanticLexiconIndexClient(null!, new StubEmbeddingGenerator()));
    }

    [Fact]
    public void Constructor_throws_when_embedding_generator_is_null()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Database=unused").Build();

        Assert.Throws<ArgumentNullException>(() => new PgVectorSemanticLexiconIndexClient(dataSource, null!));
    }

    [Fact]
    public void Constructor_defaults_options_when_none_are_supplied()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Database=unused").Build();

        _ = new PgVectorSemanticLexiconIndexClient(dataSource, new StubEmbeddingGenerator(), options: null);
    }

    [Fact]
    public async Task IndexContractAsync_throws_when_contract_is_null()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Database=unused").Build();
        var client = new PgVectorSemanticLexiconIndexClient(dataSource, new StubEmbeddingGenerator());

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.IndexContractAsync(null!));
    }

    [Fact]
    public async Task IndexEntryAsync_throws_when_entry_is_null()
    {
        using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost;Database=unused").Build();
        var client = new PgVectorSemanticLexiconIndexClient(dataSource, new StubEmbeddingGenerator());

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.IndexEntryAsync(null!));
    }

    private sealed class StubEmbeddingGenerator : ISemanticEmbeddingGenerator
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new float[] { 1f });

        public Task<IReadOnlyList<float[]>> EmbedManyAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 1f }).ToArray());
    }
}