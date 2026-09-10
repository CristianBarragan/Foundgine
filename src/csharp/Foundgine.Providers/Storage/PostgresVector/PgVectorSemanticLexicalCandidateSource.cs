using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Resolution;
using Npgsql;

namespace Foundgine.Providers.Storage.PostgresVector;

/// <summary>
/// pgvector implementation of Foundgine's provider-neutral lexical candidate
/// source. pgvector similarity search supplies ranked hypotheses only —
/// Foundgine's semantic contract remains authoritative for graph topology
/// and path legality. This class never decides what a token means; it only
/// proposes what it might mean.
/// </summary>
public sealed class PgVectorSemanticLexicalCandidateSource : ISemanticLexicalCandidateSource
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISemanticEmbeddingGenerator _embeddingGenerator;
    private readonly PgVectorOptions _options;

    public PgVectorSemanticLexicalCandidateSource(
        NpgsqlDataSource dataSource,
        ISemanticEmbeddingGenerator embeddingGenerator,
        PgVectorOptions? options = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _options = options ?? new PgVectorOptions();
    }

    public IReadOnlyList<SemanticLexicalCandidate> Retrieve(SemanticLexicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RetrieveAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    }

    public IReadOnlyList<SemanticLexicalCandidate> Retrieve(
        SemanticLexicalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RetrieveAsync(request, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<SemanticLexicalCandidate>> RetrieveAsync(
        SemanticLexicalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var queryEmbedding = await _embeddingGenerator.EmbedAsync(request.Token, cancellationToken);
        var vectorParameter = new global::Pgvector.Vector(queryEmbedding);
        var distanceOperator = DistanceOperator(_options.Distance);
        var kindNames = request.EffectiveKinds.Select(x => x.ToString()).ToArray();

        var contextFilter = request.ContextEntity is null
            ? string.Empty
            : """
              AND (
                  entity_id = $3
                  OR source_entity_id = $3
                  OR target_entity_id = $3
              )
              """;

        // Context is deliberately a retrieval hint, not a semantic
        // authorization check. The core resolver performs authoritative
        // graph compatibility validation once a candidate is proposed.
        var sql = $"""
                   SELECT
                       canonical_name, kind, entity_id, relationship_id, field_id,
                       source_entity_id, target_entity_id, value,
                       embedding {distanceOperator} $1 AS distance
                   FROM {_options.QualifiedTableName}
                   WHERE kind = ANY($2)
                   {contextFilter}
                   ORDER BY embedding {distanceOperator} $1
                   LIMIT {(request.ContextEntity is null ? "$3" : "$4")}
                   """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(vectorParameter);
        command.Parameters.AddWithValue(kindNames);

        if (request.ContextEntity is not null)
        {
            command.Parameters.AddWithValue(unchecked((long)request.ContextEntity.Value.Value));
        }

        command.Parameters.AddWithValue(request.Limit);

        var results = new List<SemanticLexicalCandidate>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var candidate = ReadCandidate(request.Token, reader, _options.Distance);
            if (candidate is not null) results.Add(candidate);
        }

        return results;
    }

    private static SemanticLexicalCandidate? ReadCandidate(
        string token,
        NpgsqlDataReader reader,
        PgVectorDistance distanceMetric)
    {
        var canonicalName = reader.GetString(0);
        if (!Enum.TryParse<SemanticLexicalCandidateKind>(reader.GetString(1), true, out var kind))
            return null;

        var distance = reader.GetDouble(8);
        var score = ToScore(distance, distanceMetric);

        return new SemanticLexicalCandidate(
            token,
            kind,
            canonicalName,
            score,
            ReadEntityId(reader, 2),
            ReadRelationshipId(reader, 3),
            ReadFieldId(reader, 4),
            ReadEntityId(reader, 5),
            ReadEntityId(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            [
                new ResolutionEvidence(
                    $"pgvector nearest-neighbor match for '{token}' " +
                    $"({distanceMetric} distance {distance:0.####}).",
                    CandidateEvidenceKind.VectorSimilarity,
                    score)
            ]);
    }

    /// <summary>
    /// Converts a raw pgvector distance into a bounded relevance score. This
    /// is provider relevance, not a probability — the same rule the
    /// Elasticsearch provider follows for its BM25 <c>_score</c>.
    /// </summary>
    internal static double ToScore(double distance, PgVectorDistance metric) => metric switch
    {
        // Cosine distance is 1 - cosine_similarity, so it already sits in
        // [0, 2]; similarity (the score) is 1 - distance.
        PgVectorDistance.Cosine => 1d - distance,

        // L2 distance is unbounded, so fold it into (0, 1].
        PgVectorDistance.L2 => 1d / (1d + distance),

        // pgvector's <#> returns the negative inner product, so negating it
        // recovers a value where "more similar" is "larger".
        PgVectorDistance.InnerProduct => -distance,

        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
    };

    internal static string DistanceOperator(PgVectorDistance distance) => distance switch
    {
        PgVectorDistance.Cosine => "<=>",
        PgVectorDistance.L2 => "<->",
        PgVectorDistance.InnerProduct => "<#>",
        _ => throw new ArgumentOutOfRangeException(nameof(distance), distance, null)
    };

    private static EntityId? ReadEntityId(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : new EntityId((ulong)reader.GetInt64(ordinal));

    private static RelationshipId? ReadRelationshipId(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : new RelationshipId((ulong)reader.GetInt64(ordinal));

    private static FieldId? ReadFieldId(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : new FieldId((ulong)reader.GetInt64(ordinal));
}