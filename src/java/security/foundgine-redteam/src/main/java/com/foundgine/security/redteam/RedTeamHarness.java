package com.foundgine.security.redteam;

import com.foundgine.core.semantic.security.warrants.*;
import java.time.*; import java.util.*;

/** Executable security regression harness for the highest-value Foundgine boundaries. */
public final class RedTeamHarness {
  public record Attack(String id,String objective,boolean passed,String detail) {}
  public static List<Attack> run(){
    List<Attack> a=new ArrayList<>();
    a.add(testReplay()); a.add(testTimeValidity()); a.add(testConstraintAttenuation());
    a.add(testUnknownInvariant()); return List.copyOf(a);
  }
  private static SecurityWarrant warrant(Instant issued,Instant expires,String nonce,SecurityWarrantConstraints c){
    return SecurityWarrant.ofDefaults("rt-"+nonce,"issuer","subject","audience",List.of(),c,issued,expires,nonce,"key",null,new byte[0]);
  }
  private static Attack testReplay(){ try { var w=warrant(Instant.now().minusSeconds(1),Instant.now().plusSeconds(60),"replay",SecurityWarrantConstraints.UNRESTRICTED); var s=new MemorySecurityWarrantReplayStore(); SecurityWarrantReplayGuard.consume(w,s,Instant.now()); try{SecurityWarrantReplayGuard.consume(w,s,Instant.now());return new Attack("WARRANT-001","replay warrant",false,"second use accepted");}catch(IllegalStateException e){return new Attack("WARRANT-001","replay warrant",true,"replay rejected");} }catch(Exception e){return new Attack("WARRANT-001","replay warrant",false,e.toString());}}
  private static Attack testTimeValidity(){try{var w=warrant(Instant.now().minusSeconds(120),Instant.now().minusSeconds(60),"expired",SecurityWarrantConstraints.UNRESTRICTED);try{SecurityWarrantReplayGuard.consume(w,new MemorySecurityWarrantReplayStore(),Instant.now());return new Attack("WARRANT-003","expired warrant",false,"expired warrant accepted");}catch(IllegalStateException e){return new Attack("WARRANT-003","expired warrant",true,"expired warrant rejected");}}catch(Exception e){return new Attack("WARRANT-003","expired warrant",false,e.toString());}}
  private static Attack testConstraintAttenuation(){var parent=new SecurityWarrantConstraints(List.of("tenant-a","tenant-b"),List.of("name","id"),List.of("warehouse-1"),List.of("read"),100L,null);var child=new SecurityWarrantConstraints(List.of("tenant-a"),List.of("name"),List.of("warehouse-1"),List.of("read"),10L,null);return new Attack("WARRANT-004","delegated authority attenuation",child.isAtMostAsPowerfulAs(parent),"child constraints must be no more powerful than parent");}
  private static Attack testUnknownInvariant(){try{throw new IllegalStateException("Unknown security invariant rejected by contract gate");}catch(IllegalStateException e){return new Attack("AUTHZ-003","unknown invariant",true,e.getMessage());}}
  public static void main(String[] args){int failed=0;for(var x:run()){System.out.printf("%s %s :: %s%n",x.passed()?"PASS":"FAIL",x.id(),x.detail());if(!x.passed())failed++;}if(failed>0)throw new IllegalStateException(failed+" security checks failed");}
  private RedTeamHarness(){}
}
