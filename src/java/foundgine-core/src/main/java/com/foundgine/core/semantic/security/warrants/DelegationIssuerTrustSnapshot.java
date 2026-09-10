package com.foundgine.core.semantic.security.warrants;
public record DelegationIssuerTrustSnapshot(String issuer,long sequence,String trustFingerprint,String keyId,DelegationIssuerKeyState keyState) {
 public void assertCurrent(ISecurityWarrantDelegationTrustStateResolver resolver) {
  var c=resolver.capture(issuer,keyId); if(c.sequence()!=sequence || !java.security.MessageDigest.isEqual(java.util.HexFormat.of().parseHex(c.trustFingerprint()),java.util.HexFormat.of().parseHex(trustFingerprint)) || c.keyState()!=keyState)
   throw new IllegalStateException("Delegation issuer trust or key lifecycle changed during execution.");
 }
}
