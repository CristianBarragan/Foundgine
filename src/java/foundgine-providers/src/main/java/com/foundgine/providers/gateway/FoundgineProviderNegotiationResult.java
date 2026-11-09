package com.foundgine.providers.gateway;
import java.util.List;
public record FoundgineProviderNegotiationResult(List<FoundgineProviderNegotiationFailure> failures) {
    public FoundgineProviderNegotiationResult { failures = List.copyOf(failures); }
    public boolean isSatisfied() { return failures.isEmpty(); }
    public void throwIfUnsatisfied() { if (!isSatisfied()) throw new FoundgineProviderNegotiationException(this); }
}
