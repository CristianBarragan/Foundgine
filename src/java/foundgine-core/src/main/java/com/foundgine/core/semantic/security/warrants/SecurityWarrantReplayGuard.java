package com.foundgine.core.semantic.security.warrants;
import java.time.Instant;
import java.util.Objects;
public final class SecurityWarrantReplayGuard {
 private SecurityWarrantReplayGuard(){}
 public static void consume(SecurityWarrant warrant,ISecurityWarrantReplayStore store,Instant now){
  Objects.requireNonNull(warrant);Objects.requireNonNull(store);Objects.requireNonNull(now);
  if(!warrant.isTimeValid(now))throw new IllegalStateException("Security warrant is expired or not yet valid.");
  if(!store.tryConsume(warrant.id(),warrant.nonce()))throw new IllegalStateException("Security warrant nonce has already been consumed.");
 }
}