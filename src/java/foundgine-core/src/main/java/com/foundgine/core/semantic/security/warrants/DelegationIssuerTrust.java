package com.foundgine.core.semantic.security.warrants;

import java.util.Map;
import java.util.Set;

public record DelegationIssuerTrust(String issuer, Set<String> signingKeyIds, boolean canDelegate, String audience,
                                    Set<String> allowedTenants, Map<String,DelegationIssuerKeyState> keyStates) {
    public DelegationIssuerTrust(String issuer, Set<String> signingKeyIds, boolean canDelegate) { this(issuer, signingKeyIds, canDelegate, null, Set.of(), Map.of()); }
    public boolean allowsKey(String keyId) { return signingKeyIds.contains(keyId); }
    public DelegationIssuerKeyState getKeyState(String keyId) { return keyStates.getOrDefault(keyId, DelegationIssuerKeyState.ACTIVE); }
    public boolean allowsAudience(String value) { return audience == null || java.util.Objects.equals(audience,value); }
    public boolean allowsTenant(String tenant) { return tenant == null || allowedTenants == null || allowedTenants.isEmpty() || allowedTenants.contains(tenant); }
}
