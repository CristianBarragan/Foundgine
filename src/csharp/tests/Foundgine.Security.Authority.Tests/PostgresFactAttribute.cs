using Xunit;

namespace Foundgine.Runtime.ControlPlane.Tests;

/// <summary>Runs only when the PostgreSQL integration connection is configured.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(PostgresConnectionString))
            Skip =
                "Set FOUNDGINE_POSTGRES_CONNECTION (or FOUNDGINE_POSTGRES_CONNECTION_STRING) to run this PostgreSQL integration test.";
    }

    public static string? PostgresConnectionString =>
        Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION")
        ?? Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING");
}