package com.foundgine.core.semantic.security.warrants;
import java.nio.charset.StandardCharsets; import java.security.MessageDigest; import java.util.*;
public final class MemorySecurityWarrantDelegationTrustStateStore implements ISecurityWarrantDelegationTrustStateResolver {
 private final Object gate=new Object(); private final Map<String,DelegationIssuerTrust> trust=new HashMap<>(); private long sequence;
 public long currentSequence(){ synchronized(gate){return sequence;} }
 public DelegationIssuerTrust resolve(String issuer){ synchronized(gate){return trust.get(issuer);} }
 public void set(DelegationIssuerTrust value){if(value==null||value.issuer()==null||value.issuer().isBlank())throw new IllegalArgumentException("Issuer is required."); synchronized(gate){trust.put(value.issuer(),value);sequence++;}}
 public void remove(String issuer){synchronized(gate){trust.remove(issuer);sequence++;}}
 public DelegationIssuerTrustSnapshot capture(String issuer,String keyId){synchronized(gate){var t=trust.get(issuer);if(t==null)throw new IllegalStateException("Delegation issuer '"+issuer+"' is not trusted.");if(!t.allowsKey(keyId))throw new IllegalStateException("Delegation key is not trusted by the issuer.");return new DelegationIssuerTrustSnapshot(issuer,sequence,fingerprint(t),keyId,t.getKeyState(keyId));}}
 private static String fingerprint(DelegationIssuerTrust t){var keys=new ArrayList<>(t.signingKeyIds());keys.sort(String::compareTo);var states=new ArrayList<String>();t.keyStates().entrySet().stream().sorted(Map.Entry.comparingByKey()).forEach(e->states.add(e.getKey()+"="+e.getValue()));var tenants=new ArrayList<>(t.allowedTenants()==null?Set.of():t.allowedTenants());tenants.sort(String::compareTo);String canonical=String.join("\n",t.issuer(),t.canDelegate()?"1":"0",t.audience()==null?"":t.audience(),String.join("\u001f",keys),String.join("\u001f",states),String.join("\u001f",tenants));try{return java.util.HexFormat.of().withUpperCase().formatHex(MessageDigest.getInstance("SHA-256").digest(canonical.getBytes(StandardCharsets.UTF_8)));}catch(Exception e){throw new IllegalStateException(e);}}
}
