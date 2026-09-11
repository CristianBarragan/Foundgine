package com.foundgine.core.semantic.security.warrants;
import java.time.Instant;
public interface ISecurityWarrantDelegationCompromiseStore { long currentSequence(); SecurityWarrantDelegationCompromise compromise(SecurityWarrant root,Instant now,String compromisedIssuer,String compromisedKeyId); boolean isCompromised(SecurityWarrant w); boolean isCompromisedByAncestor(SecurityWarrant w); boolean isCompromisedByIssuerOrKey(SecurityWarrant w); }
