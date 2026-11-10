using Npgsql;

namespace Foundgine.Providers.EventStore.Schema;

public static class EventStoreMigrator
{
    // Ordered, append-only. Never edit an applied migration; add a new one.
    private static readonly string[] Migrations = ["001_initial", "002_correlation", "003_outbox", "004_gateway_approvals"];

    public static async Task ApplyAsync(
        NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Session-level lock so concurrent hosts starting together do not race the migrations.
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_lock(hashtextextended('foundgine_event_store_migrations', 0));", connection))
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        try
        {
            await using (var bootstrap = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS foundgine_schema_migrations
                (version varchar(100) PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());
                """, connection))
                await bootstrap.ExecuteNonQueryAsync(cancellationToken);

            foreach (var version in Migrations)
                await ApplyOneAsync(connection, version, cancellationToken);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtextextended('foundgine_event_store_migrations', 0));", connection);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task ApplyOneAsync(
        NpgsqlConnection connection, string version, CancellationToken cancellationToken)
    {
        await using (var check = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM foundgine_schema_migrations WHERE version = @version);", connection))
        {
            check.Parameters.AddWithValue("version", version);
            if ((bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false)) return;
        }

        var assembly = typeof(EventStoreMigrator).Assembly;
        await using var stream = assembly.GetManifestResourceStream(
            $"Foundgine.Providers.EventStore.Schema.{version}.sql")
            ?? throw new InvalidOperationException($"Embedded event-store migration '{version}' was not found.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var apply = new NpgsqlCommand(sql, connection, transaction))
            await apply.ExecuteNonQueryAsync(cancellationToken);
        await using (var mark = new NpgsqlCommand(
            "INSERT INTO foundgine_schema_migrations(version) VALUES (@version) ON CONFLICT DO NOTHING;",
            connection, transaction))
        {
            mark.Parameters.AddWithValue("version", version);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
