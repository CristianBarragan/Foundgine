package com.foundgine.benchmark;

import jakarta.servlet.FilterChain; import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest; import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException; import java.nio.charset.StandardCharsets; import java.security.MessageDigest; import java.util.HexFormat;
import org.springframework.security.core.Authentication; import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationToken; import org.springframework.web.filter.OncePerRequestFilter;
public final class BaselineRevocationFilter extends OncePerRequestFilter {
 private final BaselineRevocationEpochGuard guard; public BaselineRevocationFilter(BaselineRevocationEpochGuard g){guard=g;}
 @Override protected boolean shouldNotFilter(HttpServletRequest r){return !r.getRequestURI().startsWith("/graphql");}
 @Override protected void doFilterInternal(HttpServletRequest r,HttpServletResponse response,FilterChain chain)throws ServletException,IOException {
  Authentication a=SecurityContextHolder.getContext().getAuthentication(); if(!(a instanceof JwtAuthenticationToken jwt)){response.sendError(401);return;}
  try {String digest=HexFormat.of().withUpperCase().formatHex(MessageDigest.getInstance("SHA-256").digest(jwt.getToken().getTokenValue().getBytes(StandardCharsets.UTF_8))); String id=jwt.getToken().getId()==null?jwt.getName():jwt.getToken().getId(); guard.validate(id,digest); chain.doFilter(r,response);}
  catch(SecurityException e){response.sendError(401,"Bearer token revoked");}
  catch(Exception e){response.sendError(503,"Revocation state unavailable; failing closed");}
 }
}
