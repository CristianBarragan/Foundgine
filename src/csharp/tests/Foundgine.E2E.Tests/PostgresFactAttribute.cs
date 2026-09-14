using Xunit;

namespace Foundgine.E2E.Tests;

/// <summary>Runs a PostgreSQL integration test only when the benchmark connection is configured.</summary>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")))
            Skip = "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run this PostgreSQL integration test.";
    }
}

/// <summary>Runs a PostgreSQL theory only when the benchmark connection is configured.</summary>
public sealed class PostgreSqlTheoryAttribute : TheoryAttribute
{
    public PostgreSqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING")))
            Skip = "Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run this PostgreSQL integration theory.";
    }
}