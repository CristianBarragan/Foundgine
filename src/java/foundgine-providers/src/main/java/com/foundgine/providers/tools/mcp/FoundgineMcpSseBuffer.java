package com.foundgine.providers.tools.mcp;

import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;
import java.util.Objects;

/**
 * Bounded, replayable event log for one MCP session's server-to-client SSE stream (spec {@code mcp-sse-replay-v1}).
 *
 * <p><b>Event ids</b> are the decimal sequence number, starting at 1 and never reused within a buffer. They carry no
 * authority: a buffer belongs to exactly one session (see {@link FoundgineMcpSseRegistry}) and the transport must have
 * admitted the session ({@link FoundgineMcpSessionGate#admitGet}) before touching it.
 *
 * <p><b>Replay.</b> {@link #replayAfter} returns every retained event after {@code Last-Event-ID}. A missing id yields
 * nothing. An id that is not a number, or is ahead of anything this buffer ever issued, is {@link ReplayStatus#INVALID}. An
 * id older than the retained window is {@link ReplayStatus#EVICTED} (events were dropped; the client must resynchronise).
 *
 * <p><b>Bounds.</b> At most {@code maxEvents} events and {@code maxBytes} of UTF-8 payload, oldest evicted first. A single
 * event larger than {@code maxBytes} is refused. Thread-safe.
 *
 * <p><b>One live stream.</b> {@link #subscribe} gives at most one wake-up callback at a time (MCP: a message must not be sent
 * on more than one stream). The callback is only a nudge, called after each successful append and outside this buffer's
 * lock; the stream then reads the buffer itself with {@link #replayAfter}, so ordering and de-duplication come from the
 * sequence numbers and a slow or failing callback can never delay or corrupt an append.
 */
public final class FoundgineMcpSseBuffer {
    public record Event(long sequence, String data) {
        /** The SSE {@code id} field value. */
        public String id() { return Long.toString(sequence); }
    }

    public enum ReplayStatus { OK, EVICTED, INVALID }

    public record Replay(ReplayStatus status, List<Event> events) {}

    private final int maxEvents;
    private final long maxBytes;
    private final Deque<Event> events = new ArrayDeque<>();
    private long bytes;
    private long lastSequence;
    private Subscription active;
    private boolean retired;

    /** A claimed live-stream slot. {@link #close} releases it (idempotent; a stale handle never frees a newer one). */
    public final class Subscription implements AutoCloseable {
        private final Runnable wake;
        private final Runnable onRetire;
        private Subscription(Runnable wake, Runnable onRetire) { this.wake = wake; this.onRetire = onRetire; }
        @Override public void close() {
            synchronized (FoundgineMcpSseBuffer.this) { if (active == this) active = null; }
        }
    }

    public FoundgineMcpSseBuffer(int maxEvents, long maxBytes) {
        if (maxEvents <= 0) throw new IllegalArgumentException("Maximum events must be positive.");
        if (maxBytes <= 0) throw new IllegalArgumentException("Maximum bytes must be positive.");
        this.maxEvents = maxEvents;
        this.maxBytes = maxBytes;
    }

    /** @throws IllegalArgumentException when {@code data} is null or on its own exceeds the byte bound */
    public Event append(String data) {
        Objects.requireNonNull(data, "data");
        long size = data.getBytes(StandardCharsets.UTF_8).length;
        if (size > maxBytes) throw new IllegalArgumentException("The event is larger than the buffer's byte limit.");
        Event e;
        Runnable wake;
        synchronized (this) {
            e = new Event(++lastSequence, data);
            events.addLast(e);
            bytes += size;
            while (events.size() > maxEvents || bytes > maxBytes) {
                Event old = events.removeFirst();
                bytes -= old.data().getBytes(StandardCharsets.UTF_8).length;
            }
            wake = active == null ? null : active.wake;
        }
        if (wake != null) {
            try { wake.run(); } catch (RuntimeException ignored) { /* a nudge must never fail an append */ }
        }
        return e;
    }

    /**
     * Claims the single live-stream slot.
     *
     * @param onAppend a cheap, non-blocking nudge (hand the work to another thread); never called under this buffer's lock
     * @param onRetire called once, outside the lock, if the buffer is retired while this slot is held
     * @return the slot, or {@code null} when a stream is already attached or the buffer has been retired
     */
    public synchronized Subscription subscribe(Runnable onAppend, Runnable onRetire) {
        Objects.requireNonNull(onAppend, "onAppend");
        Objects.requireNonNull(onRetire, "onRetire");
        if (active != null || retired) return null;
        active = new Subscription(onAppend, onRetire);
        return active;
    }

    /** Whether {@link #retire()} has been called. */
    public synchronized boolean isRetired() { return retired; }

    /**
     * Ends the buffer's life (its session ended or it was dropped): the holder of the live-stream slot, if any, is told and the
     * slot is freed for good, so a stream can never sit on a buffer that no longer receives anything. The retained events stay
     * readable. Idempotent.
     */
    public void retire() {
        Subscription holder;
        synchronized (this) {
            retired = true;
            holder = active;
            active = null;
        }
        if (holder != null) {
            try { holder.onRetire.run(); } catch (RuntimeException ignored) { /* the owner is gone anyway */ }
        }
    }

    /** The sequence of the newest event ever appended, or 0. */
    public synchronized long lastSequence() { return lastSequence; }

    public synchronized int size() { return events.size(); }

    /** @param lastEventId the raw {@code Last-Event-ID} header, or {@code null}/blank for none */
    public synchronized Replay replayAfter(String lastEventId) {
        String trimmed = lastEventId == null ? "" : trimOws(lastEventId);
        if (trimmed.isEmpty()) return new Replay(ReplayStatus.OK, List.of());
        long after = parse(trimmed);
        if (after < 0 || after > lastSequence) return new Replay(ReplayStatus.INVALID, List.of());
        long oldestRetained = events.isEmpty() ? lastSequence + 1 : events.peekFirst().sequence();
        if (after < oldestRetained - 1) return new Replay(ReplayStatus.EVICTED, List.of());
        List<Event> out = new ArrayList<>();
        for (Event e : events) if (e.sequence() > after) out.add(e);
        return new Replay(ReplayStatus.OK, List.copyOf(out));
    }

    /** Trims only space and tab (HTTP optional whitespace); runtimes differ on Unicode whitespace, so neither uses it. */
    private static String trimOws(String v) {
        int start = 0, end = v.length();
        while (start < end && (v.charAt(start) == ' ' || v.charAt(start) == '\t')) start++;
        while (end > start && (v.charAt(end - 1) == ' ' || v.charAt(end - 1) == '\t')) end--;
        return v.substring(start, end);
    }

    private static long parse(String s) {
        if (s.isEmpty() || s.length() > 18) return -1;
        for (int i = 0; i < s.length(); i++) if (s.charAt(i) < '0' || s.charAt(i) > '9') return -1;
        return Long.parseLong(s);
    }

    /**
     * One SSE frame: {@code id:}, one {@code data:} line per payload line (embedded newlines can never end the event early
     * or inject fields), then the blank line. {@code \r\n}, {@code \n} and {@code \r} are all treated as line breaks.
     */
    public static String format(Event event) {
        StringBuilder sb = new StringBuilder("id: ").append(event.id()).append('\n');
        for (String line : event.data().split("\r\n|\n|\r", -1)) sb.append("data: ").append(line).append('\n');
        return sb.append('\n').toString();
    }

    /** Frames for all events, concatenated. */
    public static String format(List<Event> events) {
        StringBuilder sb = new StringBuilder();
        for (Event e : events) sb.append(format(e));
        return sb.toString();
    }
}
