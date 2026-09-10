package com.foundgine.core.semantic.resolution;

public final class GroundingRetrievalTimeoutException extends RuntimeException {
    private final String token;
    private final long elapsedMillis;
    public GroundingRetrievalTimeoutException(String token, long elapsedMillis) {
        super("Candidate retrieval for token '" + token + "' exceeded the retrieval timeout after " + elapsedMillis + "ms.");
        this.token = token; this.elapsedMillis = elapsedMillis;
    }
    public String token() { return token; }
    public long elapsedMillis() { return elapsedMillis; }
}
