package com.foundgine.runtime.capabilities;

import com.foundgine.runtime.FoundgineOptions;

/** Java fluent equivalent of the C# AuditEvidence extension methods. */
public final class AuditEvidenceFoundgineOptionsExtensions {
    private AuditEvidenceFoundgineOptionsExtensions() {}
    public static FoundgineOptions useAuditEvidence(FoundgineOptions options) {
        return options.enable(AuditEvidence.class, new AuditEvidence());
    }
    public static FoundgineOptions disableAuditEvidence(FoundgineOptions options) {
        return options.disable(AuditEvidence.class);
    }
}
