package com.foundgine.core.semantic.security.warrants;
import java.util.Map; import java.util.concurrent.ConcurrentHashMap;
public final class SecurityWarrantDelegationStateMachine {
 public enum State { ACTIVE, REVOKED, COMPROMISED }
 public record Snapshot(String warrantId,String warrantDigest,State state,long sequence,String activeKeyId) {}
 public record Transition(Snapshot before,Snapshot after,String operation) {}
 private static final class Cell { final Object gate=new Object(); State state=State.ACTIVE; long sequence; String activeKeyId; }
 private final Map<String,Cell> cells=new ConcurrentHashMap<>();
 public Snapshot register(SecurityWarrant w,String activeKeyId){if(w==null)throw new NullPointerException();var c=cells.computeIfAbsent(key(w),k->new Cell());synchronized(c.gate){if(c.sequence!=0)throw new IllegalStateException("Warrant is already registered in the delegation state machine.");c.sequence=1;c.activeKeyId=activeKeyId;return snapshot(w,c);}}
 public Transition revoke(SecurityWarrant w){return transition(w,"revoke",s->s==State.ACTIVE,c->c.state=State.REVOKED);}
 public Transition compromise(SecurityWarrant w){return transition(w,"compromise",s->s!=State.COMPROMISED,c->c.state=State.COMPROMISED);}
 public Transition rotateKey(SecurityWarrant w,String newKeyId){if(newKeyId==null||newKeyId.isBlank())throw new IllegalArgumentException("Key id is required.");return transition(w,"rotate-key",s->s==State.ACTIVE,c->c.activeKeyId=newKeyId);}
 public Snapshot assertCanDelegate(SecurityWarrant w){var c=get(w);synchronized(c.gate){if(c.state!=State.ACTIVE)throw new IllegalStateException("A revoked or compromised warrant cannot delegate.");return snapshot(w,c);}}
 public Snapshot read(SecurityWarrant w){var c=get(w);synchronized(c.gate){return snapshot(w,c);}}
 private Transition transition(SecurityWarrant w,String op,java.util.function.Predicate<State> allowed,java.util.function.Consumer<Cell> apply){if(w==null)throw new NullPointerException();var c=get(w);synchronized(c.gate){var b=snapshot(w,c);if(!allowed.test(c.state))throw new IllegalStateException("Illegal delegation state transition '"+op+"' from "+c.state+".");apply.accept(c);c.sequence++;return new Transition(b,snapshot(w,c),op);}}
 private Cell get(SecurityWarrant w){var c=cells.get(key(w));if(c==null)throw new IllegalStateException("Warrant is not registered in the delegation state machine.");return c;}private static Snapshot snapshot(SecurityWarrant w,Cell c){return new Snapshot(w.id(),w.digest(),c.state,c.sequence,c.activeKeyId);}private static String key(SecurityWarrant w){return w.id()+"\u001f"+w.digest();}
}
