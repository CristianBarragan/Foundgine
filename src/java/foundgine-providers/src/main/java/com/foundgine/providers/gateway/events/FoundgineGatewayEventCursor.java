package com.foundgine.providers.gateway.events;

/** Pure cursor policy for the event stream, kept separate from any transport so it is unit-testable. */
public final class FoundgineGatewayEventCursor {
    private FoundgineGatewayEventCursor() { }

    /** @param raw {@code Last-Event-ID} header or {@code after} query value; empty means "live events only". */
    public static FoundgineGatewayCursorResolution resolve(String raw, long latestSequence, long maxReplayWindow) {
        if (raw == null || raw.isEmpty())
            return new FoundgineGatewayCursorResolution(FoundgineGatewayCursorStatus.LIVE, latestSequence);
        long after;
        if (!isAsciiDigits(raw)) return new FoundgineGatewayCursorResolution(FoundgineGatewayCursorStatus.INVALID, 0);
        try { after = Long.parseLong(raw); }
        catch (NumberFormatException overflow) { return new FoundgineGatewayCursorResolution(FoundgineGatewayCursorStatus.INVALID, 0); }
        // A cursor ahead of the journal comes from a previous process-local journal: the hub restarts replay at 0.
        long effectiveLag = after > latestSequence ? latestSequence : latestSequence - after;
        return effectiveLag > maxReplayWindow
                ? new FoundgineGatewayCursorResolution(FoundgineGatewayCursorStatus.TOO_OLD, latestSequence)
                : new FoundgineGatewayCursorResolution(FoundgineGatewayCursorStatus.REPLAY, after);
    }
    private static boolean isAsciiDigits(String s) {
        for (int i = 0; i < s.length(); i++) { char c = s.charAt(i); if (c < '0' || c > '9') return false; }
        return true;
    }
}
