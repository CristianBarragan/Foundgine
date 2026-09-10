package com.foundgine.core.semantic.security.warrants;
import java.math.BigDecimal; import java.util.*;
public record SecurityWarrantConstraints(List<String> allowedTenants,List<String> allowedFields,List<String> resourceScopes,List<String> allowedOperations,Long maxResults,BigDecimal maxAmount){
 public static final SecurityWarrantConstraints UNRESTRICTED=new SecurityWarrantConstraints(List.of(),List.of(),List.of(),List.of(),null,null);
 public SecurityWarrantConstraints(Collection<String>a,Collection<String>f,Collection<String>r,Collection<String>o,Long mr,BigDecimal ma){this(norm(a),norm(f),norm(r),norm(o),nonNeg(mr,"maxResults"),nonNeg(ma,"maxAmount"));}
 public SecurityWarrantConstraints(){this((Collection<String>)null,(Collection<String>)null,(Collection<String>)null,(Collection<String>)null,null,null);}
 private static List<String> norm(Collection<String>v){return v==null?List.of():v.stream().filter(x->x!=null&&!x.isBlank()).distinct().sorted().toList();}
 private static Long nonNeg(Long v,String n){if(v!=null&&v<0)throw new IllegalArgumentException(n+" must not be negative.");return v;}private static BigDecimal nonNeg(BigDecimal v,String n){if(v!=null&&v.signum()<0)throw new IllegalArgumentException(n+" must not be negative.");return v;}
 public boolean isAtMostAsPowerfulAs(SecurityWarrantConstraints p){if(p==null)throw new NullPointerException("parent");return subset(allowedTenants,p.allowedTenants)&&subset(allowedFields,p.allowedFields)&&subset(resourceScopes,p.resourceScopes)&&subset(allowedOperations,p.allowedOperations)&&atMost(maxResults,p.maxResults)&&atMost(maxAmount,p.maxAmount);}
 private static boolean subset(List<String>c,List<String>p){return p.isEmpty()||(!c.isEmpty()&&p.containsAll(c));}private static boolean atMost(Long c,Long p){return p==null||(c!=null&&c<=p);}private static boolean atMost(BigDecimal c,BigDecimal p){return p==null||(c!=null&&c.compareTo(p)<=0);}
}
