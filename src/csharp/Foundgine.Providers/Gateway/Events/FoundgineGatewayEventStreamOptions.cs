using System.Globalization;

namespace Foundgine.Providers.Gateway.Events;

/// <summary>Configuration of the SSE event stream (<c>GET /foundgine/events</c>).</summary>
public sealed class FoundgineGatewayEventStreamOptions
{
    private byte[]? _scopeDigestKey;

    /// <summary>Interval of keep-alive comments, which also checkpoint the resume cursor past events the caller cannot see.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Delay advertised to browsers through the SSE <c>retry</c> field.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Largest number of sequence values a reconnecting client may lag behind. Beyond it the stream answers
    /// <c>410 Gone</c> with <c>X-Foundgine-Latest-Sequence</c>; the client takes a fresh snapshot and reconnects.
    /// </summary>
    public long MaxReplayWindow { get; set; } = 10_000;

    /// <summary>Authorization policy whose callers may see unscoped system events. <c>null</c> (default): nobody.</summary>
    public string? OperatorPolicy { get; set; }

    public ReadOnlyMemory<byte>? ScopeDigestKey
    {
        get
        {
            if (_scopeDigestKey is null)
                return null;

            return new ReadOnlyMemory<byte>(_scopeDigestKey);
        }
    }

    /// <summary>Sets the HMAC key (at least 32 bytes) for scope digests. Must be identical on all replicas.</summary>
    public FoundgineGatewayEventStreamOptions SetScopeDigestKey(ReadOnlySpan<byte> key)
    {
        if (key.Length < 32) throw new ArgumentException("The scope digest key must be at least 32 bytes.", nameof(key));
        _scopeDigestKey = key.ToArray();
        return this;
    }

    internal void Validate()
    {
        if (HeartbeatInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval));
        if (ReconnectDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ReconnectDelay));
        if (MaxReplayWindow < 1) throw new ArgumentOutOfRangeException(nameof(MaxReplayWindow));
    }
}

public enum FoundgineGatewayCursorStatus { Live, Replay, Invalid, TooOld }

public readonly record struct FoundgineGatewayCursorResolution(FoundgineGatewayCursorStatus Status, long After);

/// <summary>Pure cursor policy for the event stream, kept separate from HTTP so it is unit-testable.</summary>
public static class FoundgineGatewayEventCursor
{
    /// <param name="raw"><c>Last-Event-ID</c> header or <c>after</c> query value; empty means "live events only".</param>
    /// <param name="latestSequence"></param>
    /// <param name="maxReplayWindow"></param>
    public static FoundgineGatewayCursorResolution Resolve(string? raw, long latestSequence, long maxReplayWindow)
    {
        if (string.IsNullOrEmpty(raw))
            return new(FoundgineGatewayCursorStatus.Live, latestSequence);
        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var after))
            return new(FoundgineGatewayCursorStatus.Invalid, 0);

        // A cursor ahead of the journal comes from a previous process-local journal: the hub restarts replay at 0.
        var effectiveLag = after > latestSequence ? latestSequence : latestSequence - after;
        return effectiveLag > maxReplayWindow
            ? new(FoundgineGatewayCursorStatus.TooOld, latestSequence)
            : new(FoundgineGatewayCursorStatus.Replay, after);
    }
}
