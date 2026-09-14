using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Resolution;
using Npgsql;

namespace Foundgine.Providers.Storage.PostgresVector;

/// <summary>
/// Indexes the derived semantic lexicon projection into a pgvector-backed
/// table. The table is a retrieval projection only — a searchable copy of
/// canonical names, aliases, and embeddings derived from a frozen
/// <see cref="SemanticContractSnapshot"/>. Graph topology (which entities,
/// relationships, and fields exist and how they connect) stays owned by the
/// semantic contract; this table never becomes the authority for it.
/// </summary>
public sealed class PgVectorSemanticLexiconIndexClient
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISemanticEmbeddingGenerator _embeddingGenerator;
    private readonly PgVectorOptions _options;

    public PgVectorSemanticLexiconIndexClient(
        NpgsqlDataSource dataSource,
        ISemanticEmbeddingGenerator embeddingGenerator,
        PgVectorOptions? options = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _options = options ?? new PgVectorOptions();
    }

    /// <summary>
    /// Creates the vector extension, table, and an approximate-nearest-neighbor
    /// index if they do not already exist. Safe to call on every startup.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var table = _options.QualifiedTableName;
        var vectorOps = VectorOpsClass(_options.Distance);

        await using var extension = _dataSource.CreateCommand("CREATE EXTENSION IF NOT EXISTS vector");
        await extension.ExecuteNonQueryAsync(cancellationToken);

        var createTable = $"""
                           CREATE TABLE IF NOT EXISTS {table} (
                               id                bigserial PRIMARY KEY,
                               canonical_name    text NOT NULL,
                               kind              text NOT NULL,
                               search_text       text NOT NULL,
                               aliases           text[] NOT NULL DEFAULT ARRAY[]::text[],
                               description       text NULL,
                               entity_id         bigint NULL,
                               relationship_id   bigint NULL,
                               field_id          bigint NULL,
                               source_entity_id  bigint NULL,
                               target_entity_id  bigint NULL,
                               value             text NULL,
                               embedding         vector({_options.Dimensions}) NOT NULL
                           )
                           """;

        await using var createTableCmd = _dataSource.CreateCommand(createTable);
        await createTableCmd.ExecuteNonQueryAsync(cancellationToken);

        var createKindIndex = $"""
                               CREATE INDEX IF NOT EXISTS
                                   {Identifier(_options.TableName + "_kind_idx")}
                                   ON {table} (kind)
                               """;
        await using var kindIndexCmd = _dataSource.CreateCommand(createKindIndex);
        await kindIndexCmd.ExecuteNonQueryAsync(cancellationToken);

        var createVectorIndex = $"""
                                 CREATE INDEX IF NOT EXISTS
                                     {Identifier(_options.TableName + "_embedding_hnsw_idx")}
                                     ON {table}
                                     USING hnsw (embedding {vectorOps})
                                 """;
        await using var vectorIndexCmd = _dataSource.CreateCommand(createVectorIndex);
        await vectorIndexCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the entire indexed projection with entries derived from the
    /// given frozen contract snapshot. This is the intended path when a new
    /// contract version is published: reproject and reindex, never edit the
    /// index in place as if it were authoritative.
    /// </summary>
    public async Task IndexContractAsync(
        SemanticContractSnapshot contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);

        await EnsureSchemaAsync(cancellationToken);

        var entries = SemanticLexiconProjection.Build(contract);
        var embeddings = await _embeddingGenerator.EmbedManyAsync(
            entries.Select(x => x.SearchText).ToArray(),
            cancellationToken);

        if (embeddings.Count != entries.Count)
        {
            throw new InvalidOperationException(
                "Embedding generator returned a different number of vectors " +
                "than lexicon entries.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var truncate = new NpgsqlCommand(
                         $"TRUNCATE TABLE {_options.QualifiedTableName}", connection, transaction))
        {
            await truncate.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            await InsertEntryAsync(connection, transaction, entries[i], embeddings[i], cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Indexes (appends) a single lexicon entry, for incremental updates.</summary>
    public async Task IndexEntryAsync(
        SemanticLexiconEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var embedding = await _embeddingGenerator.EmbedAsync(entry.SearchText, cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await InsertEntryAsync(connection, null, entry, embedding, cancellationToken);
    }

    private async Task InsertEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        SemanticLexiconEntry entry,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   INSERT INTO {_options.QualifiedTableName}
                       (canonical_name, kind, search_text, aliases, description,
                        entity_id, relationship_id, field_id, source_entity_id,
                        target_entity_id, value, embedding)
                   VALUES
                       ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
                   """;

        await using var command = transaction is null
            ? new NpgsqlCommand(sql, connection)
            : new NpgsqlCommand(sql, connection, transaction);

        command.Parameters.AddWithValue(entry.CanonicalName);
        command.Parameters.AddWithValue(entry.Kind.ToString());
        command.Parameters.AddWithValue(entry.SearchText);
        command.Parameters.AddWithValue(entry.EffectiveAliases.ToArray());
        command.Parameters.AddWithValue((object?)entry.Description ?? DBNull.Value);
        command.Parameters.AddWithValue(ToBigInt(entry.EntityId?.Value));
        command.Parameters.AddWithValue(ToBigInt(entry.RelationshipId?.Value));
        command.Parameters.AddWithValue(ToBigInt(entry.FieldId?.Value));
        command.Parameters.AddWithValue(ToBigInt(entry.SourceEntityId?.Value));
        command.Parameters.AddWithValue(ToBigInt(entry.TargetEntityId?.Value));
        command.Parameters.AddWithValue((object?)entry.Value ?? DBNull.Value);
        command.Parameters.AddWithValue(new global::Pgvector.Vector(embedding));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Foundgine identity values are 64-bit unsigned hashes; PostgreSQL
    /// bigint is signed 64-bit. This reinterprets the bit pattern rather
    /// than narrowing the value, so it round-trips exactly through
    /// <see cref="PgVectorSemanticLexicalCandidateSource"/>'s equivalent
    /// unchecked cast back to ulong.
    /// </summary>
    private static object ToBigInt(ulong? value) =>
        value.HasValue ? unchecked((long)value.Value) : DBNull.Value;

    internal static string VectorOpsClass(PgVectorDistance distance) => distance switch
    {
        PgVectorDistance.Cosine => "vector_cosine_ops",
        PgVectorDistance.L2 => "vector_l2_ops",
        PgVectorDistance.InnerProduct => "vector_ip_ops",
        _ => throw new ArgumentOutOfRangeException(nameof(distance), distance, null)
    };

    private static string Identifier(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";
}