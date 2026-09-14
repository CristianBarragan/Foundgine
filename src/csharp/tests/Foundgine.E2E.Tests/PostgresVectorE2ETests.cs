using Foundgine.Core.Abstractions;
using Foundgine.Providers.Storage.PostgresVector;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;
using Npgsql;
using Xunit;

namespace Foundgine.E2E.Tests;

/// <summary>
/// PostgreSQL + pgvector E2E physical proof for Foundgine's lexical grounding
/// pipeline:
///
/// Semantic contract -> lexicon projection -> embedding -> pgvector index
/// -> nearest-neighbor retrieval -> graph-constrained resolution.
///
/// The pgvector index/candidate source never decides what a token means; it
/// only proposes ranked hypotheses. <see cref="SemanticLexicalResolver"/>
/// remains authoritative for graph topology and path legality, which this
/// test proves end-to-end against a real PostgreSQL 17 + pgvector instance.
///
/// Skipped unless FOUNDGINE_POSTGRES_CONNECTION_STRING is configured. The
/// docker-compose image (pgvector/pgvector:pg17) is required because a
/// vanilla postgres:17 image cannot satisfy `CREATE EXTENSION vector`.
/// </summary>
public sealed class PostgresVectorE2ETests
{
    [PostgreSqlFact]
    public async Task Contract_lexicon_round_trips_through_real_pgvector_and_grounds_a_two_token_expression()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")!;

        var customer = new EntityId(1);
        var order = new EntityId(2);
        var relationshipId = RelationshipId.Create("Customer", "Orders");

        var contract = new SemanticModelBuilder()
            .Entity(order, "Order", e => e.Identity(new FieldId(2), "Id"))
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Name", typeof(string))
                .Relationship(relationshipId, "Orders", order, RelationshipCardinality.Many))
            .Build()
            .Freeze()
            .CreateSnapshot();

        // Small, collision-resistant test dimension. Production deployments
        // use whatever dimensionality their real embedding model returns
        // (see PgVectorOptions default of 1536); the pipeline being proved
        // here does not depend on a specific dimension.
        var options = new PgVectorOptions(
            TableName: $"fg_vector_lexicon_test_{Guid.NewGuid():N}",
            Dimensions: 64,
            Distance: PgVectorDistance.Cosine);

        var embeddings = new HashedBagOfWordsEmbeddingGenerator(options.Dimensions);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        try
        {
            var indexClient = new PgVectorSemanticLexiconIndexClient(dataSource, embeddings, options);
            await indexClient.IndexContractAsync(contract);

            var candidateSource = new PgVectorSemanticLexicalCandidateSource(dataSource, embeddings, options);

            // Direct retrieval proof: the relationship's own name is the
            // closest neighbor to a query built from real pgvector distance.
            var relationshipCandidates = await candidateSource.RetrieveAsync(
                new SemanticLexicalRequest("Orders", Limit: 5));

            Assert.NotEmpty(relationshipCandidates);
            var topRelationshipCandidate = relationshipCandidates[0];
            Assert.Equal("Orders", topRelationshipCandidate.CanonicalName);
            Assert.Equal(SemanticLexicalCandidateKind.Relationship, topRelationshipCandidate.Kind);
            Assert.Equal(customer, topRelationshipCandidate.SourceEntityId);
            Assert.Equal(order, topRelationshipCandidate.TargetEntityId);
            Assert.Contains(
                topRelationshipCandidate.EffectiveEvidence,
                x => x.Kind == CandidateEvidenceKind.VectorSimilarity);

            // Full pipeline proof: two lexical tokens, both grounded against
            // the same live pgvector table, resolved into one graph path
            // via the provider-neutral resolver.
            var resolver = new SemanticLexicalResolver(contract, candidateSource);
            var resolution = resolver.Resolve("Customer Orders");

            Assert.Equal(SemanticLexicalResolutionOutcome.Resolved, resolution.Outcome);
            Assert.Equal(customer, resolution.RootEntity);
            Assert.Equal(2, resolution.Steps.Count);
            Assert.Equal("Customer", resolution.Steps[0].Candidate.CanonicalName);
            Assert.Equal("Orders", resolution.Steps[1].Candidate.CanonicalName);
        }
        finally
        {
            await using var cleanup = dataSource.CreateCommand($"DROP TABLE IF EXISTS {options.QualifiedTableName}");
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [PostgreSqlFact]
    public async Task EnsureSchemaAsync_is_idempotent_and_safe_to_call_repeatedly()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")!;

        var options = new PgVectorOptions(
            TableName: $"fg_vector_schema_test_{Guid.NewGuid():N}",
            Dimensions: 8);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        try
        {
            var indexClient = new PgVectorSemanticLexiconIndexClient(
                dataSource, new HashedBagOfWordsEmbeddingGenerator(options.Dimensions), options);

            await indexClient.EnsureSchemaAsync();
            // Second call must not throw: table/index creation is guarded by
            // IF NOT EXISTS on every statement.
            await indexClient.EnsureSchemaAsync();

            await using var countCommand = dataSource.CreateCommand(
                $"SELECT count(*) FROM {options.QualifiedTableName}");
            var rowCount = (long)(await countCommand.ExecuteScalarAsync())!;
            Assert.Equal(0, rowCount);
        }
        finally
        {
            await using var cleanup = dataSource.CreateCommand($"DROP TABLE IF EXISTS {options.QualifiedTableName}");
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Deterministic, dependency-free stand-in for a real embedding model.
    /// Text is embedded as an L2-normalized bag-of-words vector hashed into a
    /// fixed number of buckets, so shared words between the query token and
    /// an indexed lexicon entry's search text increase cosine similarity —
    /// enough signal to prove the real pgvector round trip without shipping
    /// a production embedding dependency into the test suite.
    /// </summary>
    private sealed class HashedBagOfWordsEmbeddingGenerator(int dimensions) : ISemanticEmbeddingGenerator
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(Embed(text));

        public Task<IReadOnlyList<float[]>> EmbedManyAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToArray());

        private float[] Embed(string text)
        {
            var vector = new float[dimensions];
            foreach (var word in text.ToLowerInvariant().Split(
                         (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var bucket = (int)(StableHash(word) % (uint)dimensions);
                vector[bucket] += 1f;
            }

            var norm = MathF.Sqrt(vector.Sum(x => x * x));
            if (norm > 0f)
            {
                for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
            }

            return vector;
        }

        // FNV-1a. Deterministic across runs, unlike string.GetHashCode(),
        // which .NET randomizes per process.
        private static uint StableHash(string value)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in value)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }

                return hash;
            }
        }
    }
}