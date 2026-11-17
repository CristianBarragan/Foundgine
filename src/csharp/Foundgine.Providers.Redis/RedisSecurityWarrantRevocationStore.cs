using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Semantic.Security.Warrants;

namespace Foundgine.Providers.Redis;

/// <summary>
/// Shared, monotonic warrant revocation state for horizontally scaled Foundgine hosts.
/// All keys use one Redis Cluster hash slot and mutations are committed by one Lua script.
/// </summary>
/// <remarks>
/// Reads are synchronous against the configured Redis primary. Backend failures throw and therefore fail closed.
/// Redis replication/failover durability is an operational property: configure persistence and an appropriate
/// replication acknowledgement/failover policy if acknowledged revocations must survive primary loss.
/// </remarks>
public sealed class RedisSecurityWarrantRevocationStore : ISecurityWarrantRevocationStore
{
    public const string DefaultKeyPrefix = "foundgine:";

    private const string RevokeScript = """
        local existing = redis.call('HGET', KEYS[1], 'sequence')
        if existing then
          -- Match the core store: every revoke request advances the global observation sequence,
          -- while the first immutable revocation record remains authoritative.
          local current = redis.call('INCR', KEYS[3])
          redis.call('SET', KEYS[2], existing, 'NX')
          return {redis.call('HGET', KEYS[1], 'warrantId'), redis.call('HGET', KEYS[1], 'warrantDigest'), redis.call('HGET', KEYS[1], 'revokedAt'), existing, tostring(current)}
        end
        local sequence = redis.call('INCR', KEYS[3])
        redis.call('HSET', KEYS[1], 'warrantId', ARGV[1], 'warrantDigest', ARGV[2], 'revokedAt', ARGV[3], 'sequence', sequence)
        redis.call('SET', KEYS[2], sequence, 'NX')
        return {ARGV[1], ARGV[2], ARGV[3], tostring(sequence), tostring(sequence)}
        """;

    private const string CurrentSequenceScript = "return {tostring(redis.call('GET', KEYS[1]) or '0')}";
    private const string ExistsScript = "return {tostring(redis.call('EXISTS', KEYS[1]))}";
    private const string HealthProbeScript = """
        local sequence = redis.call('GET', KEYS[1]) or '0'
        local written = redis.call('SET', KEYS[2], ARGV[1], 'NX', 'PX', 5000)
        if not written then return {'0', sequence} end
        local observed = redis.call('GET', KEYS[2])
        redis.call('DEL', KEYS[2])
        if observed ~= ARGV[1] then return {'0', sequence} end
        return {'1', sequence}
        """;

    private readonly IRedisScriptBackend _backend;
    private readonly string _keyPrefix;
    private long _lastObservedSequence;

    public RedisSecurityWarrantRevocationStore(
        IRedisScriptBackend backend,
        string keyPrefix = DefaultKeyPrefix)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        if (keyPrefix.IndexOfAny(['{', '}']) >= 0)
            throw new ArgumentException("The key prefix must not contain Redis Cluster hash-tag braces.", nameof(keyPrefix));
        // The shared hash tag guarantees that every key touched by a Lua script is in one Redis Cluster slot.
        _keyPrefix = keyPrefix.TrimEnd(':') + ":{warrant-revocations}:";
    }

    public long CurrentSequence
    {
        get
        {
            try
            {
                var result = _backend.Eval(CurrentSequenceScript, [SequenceKey], []);
                return ObserveSequence(ParseSingleLong(result, "current revocation sequence"));
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw Unavailable(ex);
            }
        }
    }

    public SecurityWarrantRevocation Revoke(SecurityWarrant warrant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(warrant);
        if (string.IsNullOrWhiteSpace(warrant.Id) || string.IsNullOrWhiteSpace(warrant.Digest))
            throw new InvalidOperationException("A warrant must have an identity and digest before revocation.");

        try
        {
            var result = _backend.Eval(
                RevokeScript,
                [IdentityKey(warrant.Id, warrant.Digest), DigestKey(warrant.Digest), SequenceKey],
                [warrant.Id, warrant.Digest, now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)]);

            if (result.Count != 5 || string.IsNullOrEmpty(result[0]) || string.IsNullOrEmpty(result[1]) ||
                !long.TryParse(result[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var revokedAt) ||
                !long.TryParse(result[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0 ||
                !long.TryParse(result[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentSequence) || currentSequence < sequence)
                throw new InvalidOperationException("Redis returned an invalid warrant revocation record.");

            ObserveSequence(currentSequence);
            return new SecurityWarrantRevocation(
                result[0], result[1], DateTimeOffset.FromUnixTimeMilliseconds(revokedAt), sequence);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw Unavailable(ex);
        }
    }

    /// <summary>
    /// Performs a bounded read/write/read/delete round trip against the configured primary.
    /// This is suitable for readiness checks; it does not prove replication or failover durability.
    /// </summary>
    /// <returns>The observed monotonic revocation sequence.</returns>
    public long Probe()
    {
        var probeKey = _keyPrefix + "health:" + Guid.NewGuid().ToString("N");
        var token = Guid.NewGuid().ToString("N");
        try
        {
            var result = _backend.Eval(HealthProbeScript, [SequenceKey, probeKey], [token]);
            if (result.Count != 2 || result[0] != "1")
                throw new InvalidOperationException("Redis warrant revocation readiness probe failed its write/read verification.");
            if (!long.TryParse(result[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence) || sequence < 0)
                throw new InvalidOperationException("Redis returned an invalid sequence during the warrant revocation readiness probe.");
            return ObserveSequence(sequence);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw Unavailable(ex);
        }
    }

    public bool IsRevoked(string warrantId, string warrantDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warrantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(warrantDigest);
        return Exists(IdentityKey(warrantId, warrantDigest));
    }

    public bool IsDigestRevoked(string warrantDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warrantDigest);
        return Exists(DigestKey(warrantDigest));
    }

    private bool Exists(string key)
    {
        try
        {
            var result = _backend.Eval(ExistsScript, [key], []);
            return ParseSingleLong(result, "revocation existence result") == 1;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw Unavailable(ex);
        }
    }

    private long ObserveSequence(long sequence)
    {
        while (true)
        {
            var previous = Interlocked.Read(ref _lastObservedSequence);
            if (sequence < previous)
                throw new InvalidOperationException(
                    $"Warrant revocation sequence regressed from {previous} to {sequence}; failing closed after possible Redis state rollback.");
            if (sequence == previous || Interlocked.CompareExchange(ref _lastObservedSequence, sequence, previous) == previous)
                return sequence;
        }
    }

    private string SequenceKey => _keyPrefix + "sequence";

    private string IdentityKey(string id, string digest) =>
        _keyPrefix + "warrant:" + Hash(id + "\u001f" + digest);

    private string DigestKey(string digest) => _keyPrefix + "digest:" + Hash(digest);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static long ParseSingleLong(IReadOnlyList<string> result, string description) =>
        result.Count == 1 && long.TryParse(result[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : throw new InvalidOperationException($"Redis returned an invalid {description}.");

    private static InvalidOperationException Unavailable(Exception inner) =>
        new("Warrant revocation store unavailable; failing closed.", inner);
}
