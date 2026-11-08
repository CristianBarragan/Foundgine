package com.foundgine.providers.gateway.approvals;

import java.time.Instant;

public record FoundgineGatewayApprovalReceipt(String handle, Instant expiresAt, String planFingerprint) { }
