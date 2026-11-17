using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;
using Npgsql;

/// <summary>Plain EF/Hot Chocolate equivalent of the benchmark's shared Redis + PostgreSQL revocation epoch check.</summary>
/// <remarks>This intentionally uses direct Npgsql and StackExchange.Redis APIs; it does not depend on Foundgine providers.</remarks>
public sealed class BaselineRevocationEpochGuard(IConnectionMultiplexer redis, NpgsqlDataSource postgres)
{
    private const string SequenceKey = "benchmark:csharp:{warrant-revocations}:sequence";
    private const string ReadSql = "SELECT pg_is_in_recovery(), high_water_mark FROM foundgine_security_warrant_revocation_epoch WHERE singleton = TRUE";
    private const string AdvanceSql = "INSERT INTO foundgine_security_warrant_revocation_epoch(singleton, high_water_mark) VALUES(TRUE, @sequence) ON CONFLICT(singleton) DO UPDATE SET high_water_mark = GREATEST(foundgine_security_warrant_revocation_epoch.high_water_mark, EXCLUDED.high_water_mark) RETURNING high_water_mark";

    // One round trip: sequence, then whether the warrant identity or digest key exists. Same shape as the Foundgine lookup.
    private const string LookupScript = """
        local sequence = redis.call('GET', KEYS[1]) or '0'
        local revoked = '0'
        if redis.call('EXISTS', KEYS[2]) == 1 or redis.call('EXISTS', KEYS[3]) == 1 then revoked = '1' end
        return {tostring(sequence), revoked}
        """;
    private static readonly byte[] LookupScriptHash = SHA1.HashData(Encoding.UTF8.GetBytes(LookupScript));

    public static void Migrate(NpgsqlDataSource postgres)
    {
        using var connection = postgres.OpenConnection();
        using var command = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS foundgine_security_warrant_revocation_epoch (singleton boolean PRIMARY KEY DEFAULT TRUE CHECK (singleton = TRUE), high_water_mark bigint NOT NULL CHECK (high_water_mark >= 0)); INSERT INTO foundgine_security_warrant_revocation_epoch(singleton, high_water_mark) VALUES(TRUE, 0) ON CONFLICT(singleton) DO NOTHING;", connection);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Same call pattern as the Foundgine path: one durable read, one Redis script (by SHA1, EVAL on NOSCRIPT), and a
    /// write only when Redis is ahead of the anchor. This keeps the comparison about the query engine, not the
    /// number of revocation round trips.
    /// </summary>
    public long Validate(string? warrantId = null, string? warrantDigest = null)
    {
        if (string.IsNullOrWhiteSpace(warrantId) || string.IsNullOrWhiteSpace(warrantDigest))
            return Synchronize();

        var durable = ReadDurable();
        var keys = new RedisKey[]
        {
            SequenceKey,
            "benchmark:csharp:{warrant-revocations}:warrant:" + Hash(warrantId + "\u001f" + warrantDigest),
            "benchmark:csharp:{warrant-revocations}:digest:" + Hash(warrantDigest)
        };
        var items = EvaluateLookup(keys);
        if (items.Length != 2 || !long.TryParse(items[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) ||
            (items[1] != "0" && items[1] != "1"))
            throw new InvalidOperationException("Redis returned an invalid revocation lookup result; failing closed.");

        var anchored = Anchor(sequence, durable);
        if (items[1] == "1") throw new UnauthorizedAccessException("Bearer token revoked.");
        return anchored;
    }

    private string[] EvaluateLookup(RedisKey[] keys)
    {
        var db = redis.GetDatabase();
        RedisResult result;
        try
        {
            result = db.ScriptEvaluate(LookupScriptHash, keys, []);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("NOSCRIPT", StringComparison.OrdinalIgnoreCase))
        {
            result = db.ScriptEvaluate(LookupScript, keys, []);
        }

        return ((RedisResult[]?)result ?? []).Select(i => (string?)i ?? string.Empty).ToArray();
    }

    private long ReadDurable()
    {
        using var connection = postgres.OpenConnection();
        using var read = new NpgsqlCommand(ReadSql, connection);
        using var reader = read.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("The PostgreSQL revocation epoch row is missing; failing closed.");
        if (reader.GetBoolean(0)) throw new InvalidOperationException("The PostgreSQL revocation anchor is on a standby; failing closed.");
        return reader.GetInt64(1);
    }

    private long Synchronize()
    {
        var raw = redis.GetDatabase().StringGet(SequenceKey);
        var sequence = raw.IsNull ? 0L : long.TryParse(raw.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidOperationException("Redis returned an invalid revocation sequence; failing closed.");
        return Anchor(sequence, ReadDurable());
    }

    private long Anchor(long sequence, long durable)
    {
        if (sequence < 0) throw new InvalidOperationException("Redis returned a negative revocation sequence; failing closed.");
        if (sequence < durable) throw new InvalidOperationException("Redis revocation state is below the durable high-water mark; failing closed.");
        // Same steady-state rule as AnchoredSecurityWarrantRevocationStore: nothing to persist when Redis and the anchor agree.
        if (sequence == durable) return sequence;
        using var connection = postgres.OpenConnection();
        using (var transaction = connection.BeginTransaction())
        {
            using (var durability = new NpgsqlCommand("SET LOCAL synchronous_commit = on", connection, transaction)) durability.ExecuteNonQuery();
            using var advance = new NpgsqlCommand(AdvanceSql, connection, transaction);
            advance.Parameters.AddWithValue("sequence", sequence);
            var committed = Convert.ToInt64(advance.ExecuteScalar(), CultureInfo.InvariantCulture);
            transaction.Commit();
            if (committed != sequence) throw new InvalidOperationException("Redis and durable revocation epoch differ; failing closed.");
        }
        using var verify = new NpgsqlCommand(ReadSql, connection);
        using var verified = verify.ExecuteReader();
        if (!verified.Read() || verified.GetBoolean(0) || verified.GetInt64(1) != sequence)
            throw new InvalidOperationException("PostgreSQL revocation anchor read-after-write failed; failing closed.");
        return sequence;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public long Probe()
    {
        var db = redis.GetDatabase();
        var key = SequenceKey + ":health:" + Guid.NewGuid().ToString("N");
        var token = Guid.NewGuid().ToString("N");
        if (!db.StringSet(key, token, TimeSpan.FromSeconds(5), When.NotExists) || db.StringGet(key) != token)
            throw new InvalidOperationException("Redis revocation readiness probe failed; failing closed.");
        db.KeyDelete(key);
        return Validate();
    }
}
