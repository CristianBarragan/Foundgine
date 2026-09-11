package com.foundgine.core.semantic.security.warrants;
import java.util.concurrent.ConcurrentHashMap;
public final class MemorySecurityWarrantDelegationConcurrencyStore implements ISecurityWarrantDelegationConcurrencyStore {
 private static final class ParentState{long sequence;}
 private final ConcurrentHashMap<String,ParentState> parents=new ConcurrentHashMap<>();
 private final ConcurrentHashMap<String,SecurityWarrantDelegationReservation> children=new ConcurrentHashMap<>();
 private final ConcurrentHashMap<String,SecurityWarrantDelegationReservation> nonces=new ConcurrentHashMap<>();
 public SecurityWarrantDelegationConcurrencySnapshot capture(SecurityWarrant p){if(p==null)throw new NullPointerException();var s=parents.computeIfAbsent(key(p),k->new ParentState());synchronized(s){return new SecurityWarrantDelegationConcurrencySnapshot(p.id(),p.digest(),s.sequence);}}
 public SecurityWarrantDelegationReservation commitChild(SecurityWarrant p,SecurityWarrant c,SecurityWarrantDelegationConcurrencySnapshot e){
  if(p==null||c==null||e==null)throw new NullPointerException();
  SecurityWarrantDelegationChainValidator.validate(java.util.List.of(p,c),c.issuedAt()); SecurityWarrantAttenuator.attenuate(p,c,c.issuedAt());
  if(!e.parentWarrantId().equals(p.id())||!e.parentWarrantDigest().equals(p.digest()))throw new IllegalStateException("Delegation concurrency snapshot is bound to a different parent.");
  var s=parents.computeIfAbsent(key(p),k->new ParentState()); synchronized(s){
   if(s.sequence!=e.sequence())throw new IllegalStateException("Delegation parent changed concurrently; retry from a fresh parent sequence.");
   var ck=key(c);if(children.containsKey(ck))throw new IllegalStateException("The exact child warrant has already been committed.");
   var nk=p.id()+"\u001f"+p.digest()+"\u001f"+c.nonce();if(nonces.containsKey(nk))throw new IllegalStateException("The child nonce has already been committed under this parent.");
   long seq=++s.sequence;var r=new SecurityWarrantDelegationReservation(p.id(),p.digest(),c.id(),c.digest(),c.nonce(),seq);
   if(children.putIfAbsent(ck,r)!=null||nonces.putIfAbsent(nk,r)!=null)throw new IllegalStateException("Concurrent delegation fork detected; child reservation was not committed.");return r;
  }
 }
 public boolean isCommitted(SecurityWarrant c){if(c==null)throw new NullPointerException();return children.containsKey(key(c));}
 private static String key(SecurityWarrant w){return w.id()+"\u001f"+w.digest();}
}