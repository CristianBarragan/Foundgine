using Npgsql;

namespace Foundgine.Providers.Security.Warrants.Postgres;

/// <summary>Creates the durable warrant revocation epoch table. Safe to run concurrently at host startup.</summary>
public static class PostgresSecurityWarrantRevocationEpochAnchorMigrator
{
    private const string LockName = "foundgine_security_warrant_revocation_epoch_migration";

    public static void Apply(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        using var connection = dataSource.OpenConnection();
        using (var acquire = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended(@name, 0));", connection))
        {
            acquire.Parameters.AddWithValue("name", LockName);
            acquire.ExecuteNonQuery();
        }

        try
        {
            using var transaction = connection.BeginTransaction();
            using (var create = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS foundgine_security_warrant_revocation_epoch
                (
                    singleton boolean PRIMARY KEY DEFAULT TRUE CHECK (singleton = TRUE),
                    high_water_mark bigint NOT NULL CHECK (high_water_mark >= 0)
                );
                INSERT INTO foundgine_security_warrant_revocation_epoch (singleton, high_water_mark)
                VALUES (TRUE, 0)
                ON CONFLICT (singleton) DO NOTHING;
                """, connection, transaction))
                create.ExecuteNonQuery();
            transaction.Commit();
        }
        finally
        {
            using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@name, 0));", connection);
            release.Parameters.AddWithValue("name", LockName);
            release.ExecuteNonQuery();
        }
    }
}
