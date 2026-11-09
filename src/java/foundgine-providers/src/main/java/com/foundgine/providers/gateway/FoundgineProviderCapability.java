package com.foundgine.providers.gateway;
/** Provider capability flags. Kept separate from gateway discovery DTOs. */
public enum FoundgineProviderCapability {
    DURABLE_STORAGE(1L << 0), ORDERED_EVENTS(1L << 1), EVENT_REPLAY(1L << 2), TRANSACTIONAL_APPEND(1L << 3),
    TRANSACTIONAL_OUTBOX(1L << 4), DURABLE_EXECUTION(1L << 5), WORKFLOW_SIGNALS(1L << 6),
    WORKFLOW_QUERIES(1L << 7), RETRY(1L << 8), DEAD_LETTER(1L << 9), STREAMING(1L << 10), TENANT_ISOLATION(1L << 11);
    private final long bit;
    FoundgineProviderCapability(long bit) { this.bit = bit; }
    public long bit() { return bit; }
    public static long mask(FoundgineProviderCapability... capabilities) {
        long result = 0;
        if (capabilities != null) for (var c : capabilities) {
            if (c == null) throw new IllegalArgumentException("capability cannot be null");
            result |= c.bit;
        }
        return result;
    }
    public static boolean containsAll(long declared, long required) { return (declared & required) == required; }
    public static long missing(long declared, long required) { return required & ~declared; }
    public static String describe(long mask) {
        if (mask == 0) return "None";
        var names = new java.util.ArrayList<String>();
        for (var c : values()) if ((mask & c.bit) != 0) names.add(c.name());
        return String.join(", ", names);
    }
}
