package com.foundgine.core.execution;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Objects;

/** Stable fingerprint of provider-neutral execution IR. */
final class ExecutionIRFingerprint {
    private ExecutionIRFingerprint() { }

    static String create(ExecutionIR ir) {
        Objects.requireNonNull(ir, "ir");
        // Deliberately use a deterministic structural representation rather
        // than Object#toString so record implementation details cannot change
        // the fingerprint unexpectedly.
        var text = new StringBuilder();
        appendNode(text, ir.root());
        text.append("|security=");
        ir.requiredSecurityInvariants().stream().sorted().forEach(x -> text.append(x).append(';'));
        text.append("|contract=").append(ir.authorizationBinding().contractFingerprint());
        text.append("|authorization=").append(ir.authorizationBinding().authorizationFingerprint());
        return sha256(text.toString());
    }

    private static void appendNode(StringBuilder b, ExecutionIRNode n) {
        b.append(n.id()).append(':').append(n.operation()).append(':').append(n.entityId().value()).append('|');
        n.fields().forEach(f -> b.append(f.value()).append(','));
        b.append('|');
        if (n.viaRelationship() != null) b.append("r=").append(n.viaRelationship().value());
        if (n.viaConnection() != null) b.append("c=").append(n.viaConnection().value());
        b.append('|').append(n.aggregateExecutionStrategy()).append('|');
        if (n.authorization() != null) b.append(n.authorization());
        b.append('|');
        n.children().forEach(c -> appendNode(b, c));
    }

    private static String sha256(String value) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256")
                    .digest(value.getBytes(StandardCharsets.UTF_8));
            var result = new StringBuilder(digest.length * 2);
            for (byte b : digest) result.append(String.format("%02x", b));
            return result.toString();
        } catch (Exception e) {
            throw new IllegalStateException("Unable to fingerprint execution IR.", e);
        }
    }
}
