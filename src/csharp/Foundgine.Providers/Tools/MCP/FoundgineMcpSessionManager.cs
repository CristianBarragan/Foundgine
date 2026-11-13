using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Foundgine.Providers.Tools.MCP;

public enum FoundgineMcpSessionStatus { Valid, NotFound, Expired, OwnerMismatch }

/// <summary>Session lifetimes and capacity.</summary>
public sealed record FoundgineMcpSessionOptions
{
    public TimeSpan IdleTimeout { get; }
    public TimeSpan MaximumLifetime { get; }
    public int MaximumActiveSessions { get; }

    public FoundgineMcpSessionOptions(TimeSpan idleTimeout, TimeSpan maximumLifetime, int maximumActiveSessions)
    {
        if (idleTimeout < TimeSpan.FromMilliseconds(1)) throw new ArgumentOutOfRangeException(nameof(idleTimeout), "Idle timeout must be positive.");
        if (maximumLifetime < idleTimeout) throw new ArgumentOutOfRangeException(nameof(maximumLifetime), "Maximum lifetime cannot be shorter than the idle timeout.");
        if (maximumActiveSessions <= 0) throw new ArgumentOutOfRangeException(nameof(maximumActiveSessions), "Maximum active sessions must be positive.");
        IdleTimeout = idleTimeout;
        MaximumLifetime = maximumLifetime;
        MaximumActiveSessions = maximumActiveSessions;
    }

    /// <summary>30 minutes idle, 24 hours absolute, 10 000 sessions.</summary>
    public static FoundgineMcpSessionOptions Default { get; } =
        new(TimeSpan.FromMinutes(30), TimeSpan.FromHours(24), 10_000);
}

/// <summary>A validated session as seen by the caller. <c>ExpiresAt</c> is the earlier of the idle and absolute deadlines.</summary>
public sealed record FoundgineMcpSession(
    string SessionId, long CreatedAtUnixMilliseconds, long LastAccessedAtUnixMilliseconds, long ExpiresAtUnixMilliseconds);

public sealed record FoundgineMcpSessionValidation(FoundgineMcpSessionStatus Status, FoundgineMcpSession? Session)
{
    public bool IsValid => Status == FoundgineMcpSessionStatus.Valid;
}

/// <summary>What a store keeps. <c>Key</c> is a digest of the id; <c>OwnerDigest</c> is null for unbound sessions.</summary>
public sealed record FoundgineMcpStoredSession(
    string Key, string? OwnerDigest, long CreatedAtUnixMilliseconds, long LastAccessedAtUnixMilliseconds,
    long IdleExpiresAtUnixMilliseconds, long AbsoluteExpiresAtUnixMilliseconds)
{
    public long ExpiresAtUnixMilliseconds => Math.Min(IdleExpiresAtUnixMilliseconds, AbsoluteExpiresAtUnixMilliseconds);
}

/// <summary>Host-replaceable session storage. A clustered deployment needs a shared implementation; every operation must be atomic.</summary>
public interface IFoundgineMcpSessionStore
{
    /// <summary>Adds the session unless the key exists; false on a collision.</summary>
    bool TryAdd(FoundgineMcpStoredSession session);
    FoundgineMcpStoredSession? Find(string key);
    /// <summary>
    /// Atomically records an access on an existing session and returns it, or null if it no longer exists. It must never
    /// create a session and never move the idle deadline backwards.
    /// </summary>
    FoundgineMcpStoredSession? Touch(string key, long lastAccessedUnixMilliseconds, long idleExpiresAtUnixMilliseconds);
    /// <summary>Removes the session; returns whether it existed.</summary>
    bool Remove(string key);
    /// <summary>Removes sessions whose earlier deadline is at or before <paramref name="nowUnixMilliseconds"/>.</summary>
    int RemoveExpired(long nowUnixMilliseconds);
    int Count { get; }
}

/// <summary>Process-local store for single-instance hosts and tests.</summary>
public sealed class InMemoryFoundgineMcpSessionStore : IFoundgineMcpSessionStore
{
    private readonly Dictionary<string, FoundgineMcpStoredSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool TryAdd(FoundgineMcpStoredSession session) { lock (_gate) return _sessions.TryAdd(session.Key, session); }
    public FoundgineMcpStoredSession? Find(string key) { lock (_gate) return _sessions.GetValueOrDefault(key); }

    public FoundgineMcpStoredSession? Touch(string key, long lastAccessed, long idleExpires)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(key, out var s)) return null;
            var updated = s with
            {
                LastAccessedAtUnixMilliseconds = Math.Max(lastAccessed, s.LastAccessedAtUnixMilliseconds),
                IdleExpiresAtUnixMilliseconds = Math.Max(idleExpires, s.IdleExpiresAtUnixMilliseconds)
            };
            _sessions[key] = updated;
            return updated;
        }
    }

    public bool Remove(string key) { lock (_gate) return _sessions.Remove(key); }

    public int RemoveExpired(long now)
    {
        lock (_gate)
        {
            var expired = _sessions.Where(kv => kv.Value.ExpiresAtUnixMilliseconds <= now).Select(kv => kv.Key).ToList();
            foreach (var key in expired) _sessions.Remove(key);
            return expired.Count;
        }
    }

    public int Count { get { lock (_gate) return _sessions.Count; } }
}

/// <summary>
/// MCP session lifecycle: opaque identifiers, idle and absolute expiry, validation, and explicit termination.
/// </summary>
/// <remarks>
/// <para><b>A session is not authentication.</b> Holding a session id proves nothing about who is calling. Every request must
/// still be authenticated by the host and every tool still reads its identity from the host's security context provider. A
/// valid session never produces or substitutes a security context, an approval or a warrant. To make a leaked id useless to
/// another principal a session may be bound to an opaque <c>owner</c> (typically the authenticated subject) when created;
/// validation and termination then require the same owner. That is additional defence, not a replacement for authenticating
/// the request.</para>
/// <para><b>Identifiers</b> are 256 random bits, base64url without padding (43 characters from the visible-ASCII set the MCP
/// specification requires). The store never sees the id, only a SHA-256 digest, so a leaked store cannot be replayed.
/// Malformed ids are rejected before any lookup.</para>
/// <para><b>Expiry.</b> An idle timeout slides on every successful validation; an absolute maximum lifetime never slides.
/// Expired sessions are removed when found and by <see cref="PurgeExpired"/>, which the host should call periodically. The
/// number of live sessions is capped.</para>
/// <para><b>HTTP mapping</b> (header <see cref="HeaderName"/>): issue the id in the response to <c>initialize</c>; for later
/// requests treat every status other than <see cref="FoundgineMcpSessionStatus.Valid"/> identically (HTTP 404, the client must
/// re-initialize); answer <c>DELETE</c> with <see cref="Terminate"/>.</para>
/// </remarks>
public sealed class FoundgineMcpSessionManager
{
    /// <summary>The MCP streamable-HTTP header that carries the session id.</summary>
    public const string HeaderName = "Mcp-Session-Id";
    private const int IdBytes = 32;
    private const int IdLength = 43;

    private readonly FoundgineMcpSessionOptions _options;
    private readonly IFoundgineMcpSessionStore _store;
    private readonly Func<long> _clock;
    private readonly object _createLock = new();

    public FoundgineMcpSessionManager(
        FoundgineMcpSessionOptions? options = null,
        IFoundgineMcpSessionStore? store = null,
        Func<long>? unixMillisecondsClock = null)
    {
        _options = options ?? FoundgineMcpSessionOptions.Default;
        _store = store ?? new InMemoryFoundgineMcpSessionStore();
        _clock = unixMillisecondsClock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Creates a session. Call it only for a request the host has already authenticated. Throws
    /// <see cref="InvalidOperationException"/> when the cap is reached and nothing has expired.
    /// </summary>
    /// <param name="owner">Opaque principal to bind the session to, or null for no binding.</param>
    public FoundgineMcpSession Create(string? owner = null)
    {
        var ownerDigest = owner is null ? null : Digest(owner);
        lock (_createLock)
        {
            var now = _clock();
            if (_store.Count >= _options.MaximumActiveSessions)
            {
                _store.RemoveExpired(now);
                if (_store.Count >= _options.MaximumActiveSessions)
                    throw new InvalidOperationException("The MCP session limit has been reached.");
            }
            var absolute = SaturatingAdd(now, (long)_options.MaximumLifetime.TotalMilliseconds);
            var idle = Math.Min(SaturatingAdd(now, (long)_options.IdleTimeout.TotalMilliseconds), absolute);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var id = NewId();
                if (_store.TryAdd(new FoundgineMcpStoredSession(Digest(id), ownerDigest, now, now, idle, absolute)))
                    return new FoundgineMcpSession(id, now, now, idle);
            }
            throw new InvalidOperationException("Unable to allocate an MCP session id.");
        }
    }

    /// <summary>
    /// Checks the id and, if valid, slides the idle deadline. Never throws for bad input. A request that is not
    /// <see cref="FoundgineMcpSessionStatus.Valid"/> must be refused with a uniform "unknown session" response.
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="owner">The authenticated principal of this request; required if the session was bound.</param>
    public FoundgineMcpSessionValidation Validate(string? sessionId, string? owner = null)
    {
        if (!IsWellFormed(sessionId)) return new(FoundgineMcpSessionStatus.NotFound, null);
        var key = Digest(sessionId!);
        var stored = _store.Find(key);
        if (stored is null) return new(FoundgineMcpSessionStatus.NotFound, null);
        var now = _clock();
        if (!OwnerMatches(stored, owner)) return new(FoundgineMcpSessionStatus.OwnerMismatch, null);
        if (now >= stored.ExpiresAtUnixMilliseconds)
        {
            _store.Remove(key);
            return new(FoundgineMcpSessionStatus.Expired, null);
        }
        var idle = Math.Min(SaturatingAdd(now, (long)_options.IdleTimeout.TotalMilliseconds), stored.AbsoluteExpiresAtUnixMilliseconds);
        var touched = _store.Touch(key, now, idle);
        if (touched is null) return new(FoundgineMcpSessionStatus.NotFound, null);
        return new(FoundgineMcpSessionStatus.Valid, new FoundgineMcpSession(sessionId!, touched.CreatedAtUnixMilliseconds,
            touched.LastAccessedAtUnixMilliseconds, touched.ExpiresAtUnixMilliseconds));
    }

    /// <summary>
    /// Terminates a session (the MCP <c>DELETE</c>). Idempotent. Only a matching owner can terminate a bound session.
    /// </summary>
    /// <returns>True if this call ended a live session.</returns>
    public bool Terminate(string? sessionId, string? owner = null)
    {
        if (!IsWellFormed(sessionId)) return false;
        var key = Digest(sessionId!);
        var stored = _store.Find(key);
        if (stored is null || !OwnerMatches(stored, owner)) return false;
        var live = _clock() < stored.ExpiresAtUnixMilliseconds;
        return _store.Remove(key) && live;
    }

    /// <summary>
    /// Whether the session exists and has not expired, without sliding its idle deadline or checking an owner. For host-internal
    /// housekeeping (such as dropping the resources of ended sessions); never use it to admit a request.
    /// </summary>
    public bool IsLive(string? sessionId)
    {
        if (!IsWellFormed(sessionId)) return false;
        var stored = _store.Find(Digest(sessionId!));
        return stored is not null && _clock() < stored.ExpiresAtUnixMilliseconds;
    }

    /// <summary>Removes expired sessions; schedule it periodically. Returns how many were removed.</summary>
    public int PurgeExpired() => _store.RemoveExpired(_clock());

    /// <summary>Sessions currently stored, including expired ones not yet purged.</summary>
    public int ActiveSessionCount => _store.Count;

    /// <summary>
    /// A short, non-reversible identifier for audit logs, derived from the same digest the store keys on. It cannot be used
    /// to present a session. Returns null for ids that are not well formed, so attacker-controlled text never reaches a log.
    /// </summary>
    public static string? Fingerprint(string? sessionId) => IsWellFormed(sessionId) ? Digest(sessionId!)[..12] : null;

    /// <summary>True if <paramref name="id"/> has the shape this manager issues; a cheap pre-check for transports.</summary>
    public static bool IsWellFormed(string? id)
    {
        if (id is null || id.Length != IdLength) return false;
        foreach (var c in id)
        {
            var ok = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
            if (!ok) return false;
        }
        return true;
    }

    private static bool OwnerMatches(FoundgineMcpStoredSession stored, string? owner)
    {
        if (stored.OwnerDigest is null) return true;
        if (owner is null) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stored.OwnerDigest), Encoding.ASCII.GetBytes(Digest(owner)));
    }

    private static string NewId() => Base64Url(RandomNumberGenerator.GetBytes(IdBytes));
    private static string Digest(string value) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static long SaturatingAdd(long a, long b)
    {
        try { return checked(a + b); }
        catch (OverflowException) { return long.MaxValue; }
    }
}
