package com.foundgine.core.semantic.security.warrants;
public interface ISecurityWarrantDelegationTrustStateResolver extends ISecurityWarrantDelegationTrustResolver { long currentSequence(); DelegationIssuerTrustSnapshot capture(String issuer,String keyId); }
