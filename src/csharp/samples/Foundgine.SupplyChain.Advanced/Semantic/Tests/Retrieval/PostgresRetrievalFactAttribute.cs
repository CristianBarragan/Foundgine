using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests.Retrieval;

/// <summary>
/// Connection details for the opt-in PostgreSQL-backed retrieval provider
/// tests. Mirrors the pattern used by
/// Foundgine.Runtime.ControlPlane.Tests.PostgresFactAttribute: CI and local
/// runs stay green without a database, while anyone with a real PostgreSQL
/// instance (with the relevant extensions) gets full coverage of the
/// PostgresRetrievalCandidateSource providers.
/// </summary>
internal static class PostgresRetrievalTestEnvironment
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION")
        ?? Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")
        ?? "";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString);

    public static bool ApacheAgeEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_AGE"),
            "1",
            StringComparison.Ordinal);

    public static bool PgSearchEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_PGSEARCH"),
            "1",
            StringComparison.Ordinal);
}

/// <summary>
/// Runs only when a PostgreSQL integration connection is configured. Covers
/// the always-available providers: pg_trgm (Fuzzy) and native full text
/// (FullText).
/// </summary>
public sealed class PostgresRetrievalFactAttribute : FactAttribute
{
    public PostgresRetrievalFactAttribute()
    {
        if (!PostgresRetrievalTestEnvironment.IsConfigured)
        {
            Skip =
                "Set FOUNDGINE_POSTGRES_CONNECTION (or FOUNDGINE_POSTGRES_CONNECTION_STRING) " +
                "to run PostgreSQL-backed semantic retrieval tests.";
        }
    }
}

/// <summary>
/// Runs only when a PostgreSQL connection is configured AND the pg_search
/// extension has been opted into (it is not installed by default on a
/// vanilla PostgreSQL image), via FOUNDGINE_POSTGRES_PGSEARCH=1.
/// </summary>
public sealed class PgSearchFactAttribute : FactAttribute
{
    public PgSearchFactAttribute()
    {
        if (!PostgresRetrievalTestEnvironment.IsConfigured)
        {
            Skip =
                "Set FOUNDGINE_POSTGRES_CONNECTION (or FOUNDGINE_POSTGRES_CONNECTION_STRING) " +
                "to run PostgreSQL-backed semantic retrieval tests.";
        }
        else if (!PostgresRetrievalTestEnvironment.PgSearchEnabled)
        {
            Skip =
                "Set FOUNDGINE_POSTGRES_PGSEARCH=1 to run pg_search (Search/BM25) retrieval " +
                "tests against an instance with the pg_search extension installed.";
        }
    }
}

/// <summary>
/// Runs only when a PostgreSQL connection is configured AND Apache AGE has
/// been opted into (it is not installed by default on a vanilla PostgreSQL
/// image), via FOUNDGINE_POSTGRES_AGE=1.
/// </summary>
public sealed class ApacheAgeFactAttribute : FactAttribute
{
    public ApacheAgeFactAttribute()
    {
        if (!PostgresRetrievalTestEnvironment.IsConfigured)
        {
            Skip =
                "Set FOUNDGINE_POSTGRES_CONNECTION (or FOUNDGINE_POSTGRES_CONNECTION_STRING) " +
                "to run PostgreSQL-backed semantic retrieval tests.";
        }
        else if (!PostgresRetrievalTestEnvironment.ApacheAgeEnabled)
        {
            Skip =
                "Set FOUNDGINE_POSTGRES_AGE=1 to run GraphSimilarity (Apache AGE) retrieval " +
                "tests against an instance with the age extension installed.";
        }
    }
}
