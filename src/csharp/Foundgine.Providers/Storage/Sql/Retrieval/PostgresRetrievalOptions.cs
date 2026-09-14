namespace Foundgine.Providers.Storage.Sql.Retrieval;

/// <summary>PostgreSQL retrieval capabilities used to ground semantic references.</summary>
public sealed record PostgresRetrievalOptions(
    bool EnablePgTrgm = true,
    bool EnableFullText = true,
    bool EnablePgSearch = false,
    bool EnableApacheAge = false,
    string FullTextConfiguration = "english",
    string AgeGraphName = "foundgine");