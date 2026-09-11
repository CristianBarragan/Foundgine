package com.foundgine.core.semantic.security.warrants;
import java.time.Instant;
public final class SecurityWarrantTrustTransition { private SecurityWarrantTrustTransition(){}
 public static DelegationIssuerTrustSnapshot validateAndCapture(SecurityWarrant parent,SecurityWarrant child,ISecurityWarrantDelegationTrustStateResolver trust,Instant now,String tenant){SecurityWarrantDelegationTrust.verifyIssuer(parent,child,trust,now,tenant);var s=trust.capture(child.issuer(),child.keyId());if(s.keyState()!=DelegationIssuerKeyState.ACTIVE)throw new IllegalStateException("Only an active issuer key may authorize a new delegation.");return s;}
 public static void assertUnchanged(DelegationIssuerTrustSnapshot snapshot,ISecurityWarrantDelegationTrustStateResolver trust){if(snapshot==null||trust==null)throw new NullPointerException();snapshot.assertCurrent(trust);}
}
