package com.foundgine.benchmark;
import org.springframework.security.core.Authentication;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationToken;
import java.util.*;
public record Caller(String tenantId, String role, boolean readOnly) {
 public static Caller from(Authentication a) {
  if (!(a instanceof JwtAuthenticationToken t)) throw new SecurityException("Bearer JWT required");
  var jwt=t.getToken(); String tenant=jwt.getClaimAsString("tenant_id");
  if (tenant==null || tenant.isBlank()) throw new SecurityException("tenant_id claim required");
  Set<String> roles=new HashSet<>(); Object ra=jwt.getClaims().get("realm_access");
  if (ra instanceof Map<?,?> m && m.get("roles") instanceof Collection<?> rs) rs.forEach(x->roles.add(String.valueOf(x).toUpperCase(Locale.ROOT)));
  String role=roles.contains("SC-MANAGER")?"SUPPLY_CHAIN_MANAGER":roles.contains("SC-OPERATOR")?"WAREHOUSE_OPERATOR":roles.contains("SC-ANALYST")?"ANALYST":"CUSTOMER";
  return new Caller(tenant,role,Boolean.TRUE.equals(jwt.getClaim("read_only")));
 }
 public boolean canWrite(){return !readOnly && (role.equals("WAREHOUSE_OPERATOR")||role.equals("SUPPLY_CHAIN_MANAGER"));}
 public boolean canSeeRisk(){return role.equals("ANALYST")||role.equals("SUPPLY_CHAIN_MANAGER");}
}
