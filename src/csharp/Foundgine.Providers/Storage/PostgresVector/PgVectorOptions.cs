namespace Foundgine.Providers.Storage.PostgresVector;

/// <summary>Distance function used to rank pgvector candidates.</summary>
public enum PgVectorDistance : byte
{
    /// <summary><c>&lt;=&gt;</c> — cosine distance. Typical default for text embeddings.</summary>
    Cosine,

    /// <summary><c>&lt;-&gt;</c> — Euclidean (L2) distance.</summary>
    L2,

    /// <summary><c>&lt;#&gt;</c> — negative inner product.</summary>
    InnerProduct
}

/// <summary>
/// Configuration for the pgvector-backed semantic lexicon table. This table
/// is a derived retrieval projection of the frozen semantic contract — it is
/// never the source of truth for schema topology.
/// </summary>
public sealed record PgVectorOptions(
    string TableName = "foundgine_semantic_lexicon",
    int Dimensions = 1536,
    PgVectorDistance Distance = PgVectorDistance.Cosine,
    string Schema = "public")
{
    public string QualifiedTableName => $"\"{Schema}\".\"{TableName}\"";
}