package com.foundgine.providers.gateway;
public final class FoundgineProviderNegotiationException extends IllegalStateException {
    private final FoundgineProviderNegotiationResult result;
    public FoundgineProviderNegotiationException(FoundgineProviderNegotiationResult result) {
        super(buildMessage(result)); this.result = result;
    }
    public FoundgineProviderNegotiationResult result() { return result; }
    private static String buildMessage(FoundgineProviderNegotiationResult result) {
        var message = new StringBuilder("Foundgine provider capability negotiation failed:");
        for (var f : result.failures()) {
            message.append("\n - ").append(f.kind()).append(": missing ").append(FoundgineProviderCapability.describe(f.missing()));
            message.append(f.closestProviderId() == null ? " (no provider registered)" : " (closest provider '" + f.closestProviderId() + "')");
            if (f.reason() != null && !f.reason().isBlank()) message.append(" — ").append(f.reason());
        }
        return message.toString();
    }
}
