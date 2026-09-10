package com.foundgine.core.semantic.authorization;
import java.util.*;
public record SemanticAuthorizationContext(String tenantId,String role,Map<String,String> claims){
 public SemanticAuthorizationContext{claims=claims==null?Map.of():Map.copyOf(claims);}
 public SemanticAuthorizationContext(){this(null,null,Map.of());}
 public Map<String,String> safeClaims(){return claims;}
}
