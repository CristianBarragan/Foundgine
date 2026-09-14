namespace Foundgine.Providers.Storage.Sql.Mutation.Postgres;

/// <summary>
/// Describes the correlation columns a PostgreSQL mutation result must expose
/// so generated values can be mapped to logical operations.
/// </summary>
public sealed record PostgresCorrelationProjection(
    string OrdinalColumn,
    IReadOnlyList<string> ValueColumns);
