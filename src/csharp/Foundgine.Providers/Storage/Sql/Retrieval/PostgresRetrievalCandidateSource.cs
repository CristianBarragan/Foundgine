using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Resolution;
using Npgsql;

namespace Foundgine.Providers.Storage.Sql.Retrieval;

/// <summary>
/// PostgreSQL implementation of the semantic candidate boundary.
///
/// This provider generates retrieval candidates and evidence only.
/// Authorization and final relational execution remain owned by
/// Foundgine's semantic/execution pipeline.
/// </summary>
public sealed class PostgresRetrievalCandidateSource
    : IApproximateCandidateSource
{
    private readonly NpgsqlDataSource _dataSource;

    private readonly IMetadataCatalog _metadata;

    private readonly PostgresRetrievalOptions _options;

    public PostgresRetrievalCandidateSource(
        NpgsqlDataSource dataSource,
        IMetadataCatalog metadata,
        PostgresRetrievalOptions? options = null)
    {
        _dataSource =
            dataSource ??
            throw new ArgumentNullException(nameof(dataSource));

        _metadata =
            metadata ??
            throw new ArgumentNullException(nameof(metadata));

        _options = options ?? new();
    }

    public IReadOnlyList<RetrievalCandidate> Retrieve(
        SemanticRetrievalRequest request)
    {
        return RetrieveAsync(request)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<IReadOnlyList<RetrievalCandidate>> RetrieveAsync(
        SemanticRetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity =
            _metadata.GetEntity(request.EntityType);

        var field =
            ResolveField(entity, request.Field);

        return request.Strategy switch
        {
            RetrievalStrategy.Fuzzy =>
                _options.EnablePgTrgm
                    ? await ExecuteFuzzyAsync(
                        entity,
                        field,
                        request,
                        cancellationToken)
                    : throw Unsupported(
                        RetrievalStrategy.Fuzzy),

            RetrievalStrategy.FullText =>
                _options.EnableFullText
                    ? await ExecuteFullTextAsync(
                        entity,
                        field,
                        request,
                        cancellationToken)
                    : throw Unsupported(
                        RetrievalStrategy.FullText),

            RetrievalStrategy.Search =>
                _options.EnablePgSearch
                    ? await ExecutePgSearchAsync(
                        entity,
                        field,
                        request,
                        cancellationToken)
                    : throw Unsupported(
                        RetrievalStrategy.Search),

            RetrievalStrategy.GraphSimilarity =>
                _options.EnableApacheAge
                    ? await ExecuteGraphSimilarityAsync(
                        entity,
                        request,
                        cancellationToken)
                    : throw Unsupported(
                        RetrievalStrategy.GraphSimilarity),

            RetrievalStrategy.Vector =>
                throw new NotSupportedException(
                    "This field-value retrieval boundary does not implement " +
                    "vector search. For token-level lexical grounding backed " +
                    "by pgvector, use PgVectorSemanticLexicalCandidateSource " +
                    "in Foundgine.Providers.Storage.PostgresVector, which implements " +
                    "ISemanticLexicalCandidateSource directly against the " +
                    "projected semantic lexicon."),

            RetrievalStrategy.Relational =>
                [],

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(request.Strategy),
                    request.Strategy,
                    "Unknown retrieval strategy.")
        };
    }

    private async Task<IReadOnlyList<RetrievalCandidate>>
        ExecuteFuzzyAsync(
            EntityMetadata entity,
            FieldMetadata field,
            SemanticRetrievalRequest request,
            CancellationToken cancellationToken)
    {
        var table =
            Quote(entity.EffectiveStorageName);

        var column =
            Quote(GetColumnName(entity, field));

        var identity =
            Quote(GetPrimaryKeyName(entity));

        var sql =
            $"""
             SELECT
                 {identity},
                 similarity({column}, $1) AS score
             FROM {table}
             WHERE {column} % $1
             ORDER BY score DESC
             LIMIT $2
             """;

        await using var command =
            _dataSource.CreateCommand(sql);

        command.Parameters.AddWithValue(
            request.Query);

        command.Parameters.AddWithValue(
            request.Limit);

        return await ReadCandidatesAsync(
            command,
            entity.EntityId,
            field.Id,
            CandidateEvidenceKind.Trigram,
            cancellationToken);
    }

    private async Task<IReadOnlyList<RetrievalCandidate>>
        ExecuteFullTextAsync(
            EntityMetadata entity,
            FieldMetadata field,
            SemanticRetrievalRequest request,
            CancellationToken cancellationToken)
    {
        var table =
            Quote(entity.EffectiveStorageName);

        var column =
            Quote(GetColumnName(entity, field));

        var identity =
            Quote(GetPrimaryKeyName(entity));

        var config =
            QuoteLiteral(
                _options.FullTextConfiguration);

        var sql =
            $"""
             SELECT
                 {identity},
                 ts_rank_cd(
                     to_tsvector(
                         {config},
                         COALESCE({column}::text, '')
                     ),
                     websearch_to_tsquery(
                         {config},
                         $1
                     )
                 ) AS score
             FROM {table}
             WHERE to_tsvector(
                 {config},
                 COALESCE({column}::text, '')
             ) @@ websearch_to_tsquery(
                 {config},
                 $1
             )
             ORDER BY score DESC
             LIMIT $2
             """;

        await using var command =
            _dataSource.CreateCommand(sql);

        command.Parameters.AddWithValue(
            request.Query);

        command.Parameters.AddWithValue(
            request.Limit);

        return await ReadCandidatesAsync(
            command,
            entity.EntityId,
            field.Id,
            CandidateEvidenceKind.FullText,
            cancellationToken);
    }

    private async Task<IReadOnlyList<RetrievalCandidate>>
        ExecutePgSearchAsync(
            EntityMetadata entity,
            FieldMetadata field,
            SemanticRetrievalRequest request,
            CancellationToken cancellationToken)
    {
        /*
         * pg_search is deliberately isolated to the PostgreSQL provider.
         *
         * The semantic layer knows only about:
         *
         *     RetrievalStrategy.Search
         *
         * It does not know that PostgreSQL happens to implement that
         * strategy using pg_search/BM25.
         *
         * This keeps the semantic contracts provider-neutral.
         */

        var table =
            Quote(entity.EffectiveStorageName);

        var column =
            Quote(GetColumnName(entity, field));

        var identity =
            Quote(GetPrimaryKeyName(entity));

        var sql =
            $"""
             SELECT
                 {identity},
                 pdb.score({identity}) AS score
             FROM {table}
             WHERE {column} ||| $1
             ORDER BY score DESC
             LIMIT $2
             """;

        await using var command =
            _dataSource.CreateCommand(sql);

        command.Parameters.AddWithValue(
            request.Query);

        command.Parameters.AddWithValue(
            request.Limit);

        return await ReadCandidatesAsync(
            command,
            entity.EntityId,
            field.Id,
            CandidateEvidenceKind.Bm25,
            cancellationToken);
    }

    private async Task<IReadOnlyList<RetrievalCandidate>>
        ExecuteGraphSimilarityAsync(
            EntityMetadata entity,
            SemanticRetrievalRequest request,
            CancellationToken cancellationToken)
    {
        if (request.Relationship is null)
        {
            throw new ArgumentException(
                "GraphSimilarity requires Relationship.",
                nameof(request));
        }

        if (request.ReferenceIdentity is null)
        {
            throw new ArgumentException(
                "GraphSimilarity requires ReferenceIdentity.",
                nameof(request));
        }

        var relationship =
            _metadata.GetRelationship(
                request.Relationship.Value);

        var graph =
            _options.AgeGraphName;

        var sourceLabel =
            QuoteIdentifier(
                _metadata
                    .GetEntity(relationship.Source)
                    .EffectiveStorageName);

        var targetLabel =
            QuoteIdentifier(
                _metadata
                    .GetEntity(relationship.Target)
                    .EffectiveStorageName);

        var edgeLabel =
            QuoteIdentifier(
                relationship.Name);

        var reference =
            request.ReferenceIdentity
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");

        var cypher =
            $"""
             MATCH
                 (reference:{sourceLabel})
                 -[:{edgeLabel}]->
                 (neighbor)
                 <-[:{edgeLabel}]-
                 (candidate:{sourceLabel})
             WHERE
                 reference.id = "{reference}"
                 AND candidate.id <> "{reference}"
             RETURN
                 candidate.id,
                 count(*) AS score
             ORDER BY score DESC
             LIMIT {request.Limit}
             """;

        await using var load =
            _dataSource.CreateCommand(
                "LOAD 'age'");

        await load.ExecuteNonQueryAsync(
            cancellationToken);

        var sql =
            $"""
             SELECT *
             FROM cypher(
                 {QuoteLiteral(graph)},
                 $1
             ) AS (
                 record_id text,
                 score bigint
             )
             """;

        await using var command =
            _dataSource.CreateCommand(sql);

        command.Parameters.AddWithValue(
            cypher);

        return await ReadCandidatesAsync(
            command,
            entity.EntityId,
            null,
            CandidateEvidenceKind.GraphSimilarity,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<RetrievalCandidate>>
        ReadCandidatesAsync(
            NpgsqlCommand command,
            EntityId entityType,
            FieldId? field,
            CandidateEvidenceKind kind,
            CancellationToken cancellationToken)
    {
        var results =
            new List<RetrievalCandidate>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            var recordId =
                Convert.ToString(
                    reader.GetValue(0),
                    System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty;

            var score =
                reader.IsDBNull(1)
                    ? 0d
                    : Convert.ToDouble(
                        reader.GetValue(1),
                        System.Globalization.CultureInfo.InvariantCulture);

            results.Add(
                new RetrievalCandidate(
                    entityType,
                    recordId,
                    score,
                    field,
                    recordId,
                    [
                        new ResolutionEvidence(
                            $"{kind} retrieval matched semantic " +
                            $"candidate {recordId} with score " +
                            $"{score:0.####}.",
                            kind,
                            score)
                    ],
                    kind));
        }

        return results;
    }

    private FieldMetadata ResolveField(
        EntityMetadata entity,
        FieldId? fieldId)
    {
        if (fieldId is { } id)
        {
            return entity.EffectiveFields
                       .FirstOrDefault(x => x.Id == id)
                   ?? throw new KeyNotFoundException(
                       $"Field {id} is not registered " +
                       $"on entity {entity.Name}.");
        }

        return entity.EffectiveFields
                   .FirstOrDefault(x =>
                       x.ClrType == typeof(string) &&
                       x.Column is not null)
               ?? throw new InvalidOperationException(
                   $"Entity {entity.Name} has no string field " +
                   $"available for approximate retrieval.");
    }

    private string GetColumnName(
        EntityMetadata entity,
        FieldMetadata field)
    {
        if (field.Column is null)
        {
            throw new InvalidOperationException(
                $"Field {field.Name} has no storage column.");
        }

        return entity.Columns
                   .FirstOrDefault(x => x.Id == field.Column.ColumnId)
                   ?.EffectiveStorageName
               ?? throw new InvalidOperationException(
                   $"Column {field.Column.ColumnId} for field " +
                   $"{field.Name} is not registered on entity " +
                   $"{entity.Name}.");
    }

    private string GetPrimaryKeyName(
        EntityMetadata entity)
    {
        if (entity.PrimaryKey is null)
        {
            throw new InvalidOperationException(
                $"Entity {entity.Name} has no primary key metadata.");
        }

        return entity.Columns
                   .FirstOrDefault(x => x.Id == entity.PrimaryKey.ColumnId)
                   ?.EffectiveStorageName
               ?? throw new InvalidOperationException(
                   $"Primary key column " +
                   $"{entity.PrimaryKey.ColumnId} is not registered " +
                   $"on entity {entity.Name}.");
    }

    private static string Quote(
        string value)
    {
        return QuoteIdentifier(value);
    }

    private static string QuoteIdentifier(
        string value)
    {
        return "\"" +
               value.Replace("\"", "\"\"") +
               "\"";
    }

    private static string QuoteLiteral(
        string value)
    {
        return "'" +
               value.Replace("'", "''") +
               "'";
    }

    private static NotSupportedException Unsupported(
        RetrievalStrategy strategy)
    {
        return new NotSupportedException(
            $"PostgreSQL retrieval strategy " +
            $"{strategy} is disabled.");
    }
}