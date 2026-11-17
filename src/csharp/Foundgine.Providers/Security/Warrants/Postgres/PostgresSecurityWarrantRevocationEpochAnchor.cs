using Foundgine.Core.Semantic.Security.Warrants;
using Npgsql;

namespace Foundgine.Providers.Security.Warrants.Postgres;

/// <summary>
/// PostgreSQL-backed, cross-host durable high-water mark for warrant revocation sequences.
/// The supplied data source must target the writable primary, never a read replica.
/// </summary>
/// <remarks>
/// Call <see cref="PostgresSecurityWarrantRevocationEpochAnchorMigrator.Apply(NpgsqlDataSource)"/>
/// before registering this anchor for authorization traffic. Updates use a transactional
/// GREATEST upsert and force synchronous_commit=on for the transaction. Survival across primary
/// failover additionally depends on the PostgreSQL HA topology and synchronous replication policy.
/// </remarks>
public sealed class PostgresSecurityWarrantRevocationEpochAnchor : ISecurityWarrantRevocationEpochAnchor
{
    private const string ReadSql = "SELECT pg_is_in_recovery(), high_water_mark FROM foundgine_security_warrant_revocation_epoch WHERE singleton = TRUE;";
    private const string AdvanceSql = """
        INSERT INTO foundgine_security_warrant_revocation_epoch (singleton, high_water_mark)
        VALUES (TRUE, @sequence)
        ON CONFLICT (singleton) DO UPDATE
          SET high_water_mark = GREATEST(foundgine_security_warrant_revocation_epoch.high_water_mark, EXCLUDED.high_water_mark)
        RETURNING high_water_mark;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresSecurityWarrantRevocationEpochAnchor(NpgsqlDataSource dataSource) =>
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    /// <summary>Reads the authoritative value from the configured writable primary.</summary>
    public long ReadHighWaterMark()
    {
        try
        {
            using var connection = _dataSource.OpenConnection();
            using var command = new NpgsqlCommand(ReadSql, connection);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("The PostgreSQL warrant revocation epoch row is missing; run its migrator before serving traffic.");
            if (reader.GetBoolean(0))
                throw new InvalidOperationException("The PostgreSQL warrant revocation anchor connected to a standby; reads must use the writable primary.");
            var highWaterMark = reader.GetInt64(1);
            if (highWaterMark < 0)
                throw new InvalidOperationException("PostgreSQL returned a negative warrant revocation high-water mark.");
            return highWaterMark;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception exception)
        {
            throw new InvalidOperationException("The PostgreSQL warrant revocation anchor is unavailable; authorization must fail closed.", exception);
        }
    }

    /// <summary>Atomically advances the shared value and commits it before returning.</summary>
    public void AdvanceHighWaterMark(long sequence)
    {
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence), "A revocation sequence cannot be negative.");

        try
        {
            using var connection = _dataSource.OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var durability = new NpgsqlCommand("SET LOCAL synchronous_commit = on;", connection, transaction))
                durability.ExecuteNonQuery();

            long committedValue;
            using (var command = new NpgsqlCommand(AdvanceSql, connection, transaction))
            {
                command.Parameters.AddWithValue("sequence", sequence);
                committedValue = (long)(command.ExecuteScalar()
                    ?? throw new InvalidOperationException("PostgreSQL did not return the updated warrant revocation high-water mark."));
            }

            transaction.Commit();
            if (committedValue < sequence)
                throw new InvalidOperationException("PostgreSQL failed to advance the warrant revocation high-water mark.");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception exception)
        {
            throw new InvalidOperationException("The PostgreSQL warrant revocation anchor could not durably advance; authorization must fail closed.", exception);
        }
    }
}
