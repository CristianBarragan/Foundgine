package com.foundgine.samples.supplychain.advanced.authorization.claims;

import java.time.*; import java.util.*; import java.util.function.Function; import java.util.regex.Pattern;

public final class Claims {
  private Claims() {}
  public enum Severity { NONE, SUSPICIOUS, HOSTILE }
  public enum Key { SCOPE("scope"), WAREHOUSE("warehouse"), MAX_ROWS("max_rows"), REASON("reason"), CHANGE_TICKET("change_ticket"), NOT_AFTER("not_after");
    public final String wire; Key(String w){wire=w;} }
  public record RejectedClaim(String key,String value,String reason,Severity severity) { public RejectedClaim(String k,String v,String r){this(k,v,r,Severity.NONE);} }
  public record ValidationResult(Map<String,String> accepted,List<RejectedClaim> rejected,Severity spoofingSeverity,Map<String,Object> typedAccepted) {
    public boolean isSpoofingAttempt(){return spoofingSeverity!=Severity.NONE;}
    public static ValidationResult empty(){return new ValidationResult(Map.of(),List.of(),Severity.NONE,Map.of());}
  }
  public interface Validator { String key(); Parsed parse(String raw); }
  public record Parsed(boolean ok,String reason,Object value) {}
  public record Schema(String verticalName,Set<String> reserved,Set<String> hostile,Map<String,Validator> validators,String expiryKey,Duration maxExpiryHorizon,Set<String> evidenceKeys) {
    public Schema { reserved=Set.copyOf(reserved); hostile=Set.copyOf(hostile); validators=Map.copyOf(validators); evidenceKeys=Set.copyOf(evidenceKeys); }
  }
  public static Schema defaultSchema(){
    Set<String> reserved=Set.of("role","tenant","tenantid","actor","isadmin","admin","permissions","capabilities","scopes");
    Set<String> hostile=Set.of("isadmin","admin","permissions","capabilities","scopes");
    Map<String,Validator> v=new HashMap<>();
    v.put("scope",val("scope",x->x.equals("read-only")||x.equals("full")?new Parsed(true,null,x):new Parsed(false,"Must be 'read-only' or 'full'.",null)));
    v.put("warehouse",val("warehouse",x->positiveInt(x)?new Parsed(true,null,Integer.parseInt(x)):new Parsed(false,"Must be a positive integer warehouse id.",0)));
    v.put("max_rows",val("max_rows",x->intRange(x,1,10000)?new Parsed(true,null,Integer.parseInt(x)):new Parsed(false,"Must be a positive integer no greater than 10,000.",0)));
    v.put("reason",val("reason",x->x.trim().length()>=8&&x.trim().length()<=240?new Parsed(true,null,x):new Parsed(false,"Must be between 8 and 240 characters.",null)));
    Pattern ticket=Pattern.compile("^CHG-\\d{4,}$"); v.put("change_ticket",val("change_ticket",x->ticket.matcher(x).matches()?new Parsed(true,null,x):new Parsed(false,"Must match 'CHG-####' (four or more digits).",null)));
    v.put("not_after",val("not_after",x->{try{return new Parsed(true,null,OffsetDateTime.parse(x));}catch(Exception e){try{return new Parsed(true,null,Instant.parse(x));}catch(Exception ignored){return new Parsed(false,"Must be a valid ISO-8601 timestamp.",null);}}}));
    return new Schema("SupplyChain",reserved,hostile,v,"not_after",Duration.ofDays(7),Set.of("reason","change_ticket"));
  }
  public static ValidationResult validate(Map<String,String> raw,Instant now){return validate(raw,now,defaultSchema());}
  public static ValidationResult validate(Map<String,String> raw,Instant now,Schema s){
    if(raw==null||raw.isEmpty()) return ValidationResult.empty();
    List<RejectedClaim> rejected=new ArrayList<>(); Severity severity=Severity.NONE;
    for(String k:raw.keySet()) if(s.reserved().contains(k.toLowerCase(Locale.ROOT))){severity=s.hostile().contains(k.toLowerCase(Locale.ROOT))?Severity.HOSTILE:Severity.SUSPICIOUS; rejected.add(new RejectedClaim(k,raw.get(k),"Reserved identity key cannot be supplied by the client.",severity));}
    if(severity!=Severity.NONE) return new ValidationResult(Map.of(),List.copyOf(rejected),severity,Map.of());
    Map<String,String> accepted=new TreeMap<>(String.CASE_INSENSITIVE_ORDER); Map<String,Object> typed=new TreeMap<>(String.CASE_INSENSITIVE_ORDER);
    for(var e:raw.entrySet()){
      Validator validator=s.validators().get(e.getKey().toLowerCase(Locale.ROOT)); if(validator==null){rejected.add(new RejectedClaim(e.getKey(),e.getValue(),"Unrecognized claim key; ignored."));continue;}
      if(e.getValue()==null||e.getValue().isBlank()){rejected.add(new RejectedClaim(e.getKey(),e.getValue(),"Value is empty."));continue;}
      Parsed p=validator.parse(e.getValue()); if(!p.ok()){rejected.add(new RejectedClaim(e.getKey(),e.getValue(),p.reason()));continue;} accepted.put(e.getKey(),e.getValue()); typed.put(e.getKey(),p.value());
    }
    if(accepted.containsKey(s.expiryKey())){
      String rawExpiry=accepted.get(s.expiryKey()); Instant expiry=toInstant(typed.get(s.expiryKey()));
      if(expiry!=null&&(expiry.isBefore(now)||expiry.isAfter(now.plus(s.maxExpiryHorizon())))){String reason=expiry.isBefore(now)?"Associated 'not_after' claim has expired; evidence is stale.":"Associated 'not_after' claim exceeds the maximum evidence validity horizon."; accepted.remove(s.expiryKey()); typed.remove(s.expiryKey()); for(String k:s.evidenceKeys()) if(accepted.remove(k)!=null){typed.remove(k); rejected.add(new RejectedClaim(k,raw.get(k),reason));} rejected.add(new RejectedClaim(s.expiryKey(),rawExpiry,reason));}
    }
    return new ValidationResult(Map.copyOf(accepted),List.copyOf(rejected),Severity.NONE,Map.copyOf(typed));
  }
  private static Validator val(String key,Function<String,Parsed> f){return new Validator(){public String key(){return key;} public Parsed parse(String raw){return f.apply(raw);}};}
  private static boolean positiveInt(String x){return x.matches("[0-9]+")&&Integer.parseInt(x)>0;}
  private static boolean intRange(String x,int a,int b){return x.matches("[0-9]+")&&Integer.parseInt(x)>=a&&Integer.parseInt(x)<=b;}
  private static Instant toInstant(Object o){if(o instanceof Instant i)return i;if(o instanceof OffsetDateTime d)return d.toInstant();return null;}
}
