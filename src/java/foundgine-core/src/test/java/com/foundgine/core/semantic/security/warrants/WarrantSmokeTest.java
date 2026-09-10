package com.foundgine.core.semantic.security.warrants;
import java.math.BigDecimal;import java.security.*;import java.security.interfaces.RSAPublicKey;import java.time.Instant;import java.util.*;
public final class WarrantSmokeTest { public static void main(String[] args)throws Exception{
 var g=new CapabilityGrant("orders","read",new LinkedHashSet<>(List.of("tenant-a"))); if(!g.resourceScopes().equals(List.of("tenant-a")))throw new AssertionError();
 var c=new SecurityWarrantConstraints(List.of("tenant-a"),List.of("id"),List.of("tenant-a"),List.of("read"),100L,new BigDecimal("10.00")); var now=Instant.now();
 var w=SecurityWarrant.ofDefaults("w1","issuer","subject","aud",List.of(g),c,now.minusSeconds(10),now.plusSeconds(100),"n1","k1",null,new byte[0]);
 var kp=KeyPairGenerator.getInstance("RSA");kp.initialize(2048);var pair=kp.generateKeyPair();var signed=SecurityWarrantSigner.sign(w,pair.getPrivate()); SecurityWarrantVerifier.verify(signed,k->(RSAPublicKey)pair.getPublic(),now,"issuer","aud");
 if(signed.digest().length()!=64)throw new AssertionError(); if(!SecurityWarrantAuthorization.allows(signed,"subject","aud","orders","read","tenant-a","tenant-a",10L,new BigDecimal("5"),true))throw new AssertionError();
 var replay=new MemorySecurityWarrantReplayStore();if(!replay.tryConsume("w1","n1")||replay.tryConsume("w1","n1"))throw new AssertionError(); System.out.println("WARRANT_SMOKE_OK"); }}
