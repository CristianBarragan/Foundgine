package com.foundgine.samples.supplychain;

import java.util.*;

/** Deterministic, in-memory equivalents of the high-value adversarial scenarios. */
public final class Scenarios {
  public record SupplierExposure(int productId,int supplierId,int depth,boolean cycleDetected) {}
  public record FulfillmentRisk(int productId,String sku,int demand,int available,int inbound,int shortage,Set<Integer> suppliers) {}
  public static List<SupplierExposure> recursiveSupplierRisk(Map<Integer,List<Integer>> bom, int root, int maxDepth) {
    List<SupplierExposure> out=new ArrayList<>(); Set<Integer> visited=new HashSet<>(); walk(bom,root,0,maxDepth,new HashSet<>(),visited,out); return List.copyOf(out);
  }
  private static void walk(Map<Integer,List<Integer>> bom,int p,int depth,int max,Set<Integer> path,Set<Integer> visited,List<SupplierExposure> out){
    if(depth>max)return; if(!path.add(p)){out.add(new SupplierExposure(p,0,depth,true));return;} if(!visited.add(p)){path.remove(p);return;}
    for(int child:bom.getOrDefault(p,List.of())) walk(bom,child,depth+1,max,path,visited,out); path.remove(p);
  }
  public static void assertAdversarialInvariants(){
    Map<Integer,List<Integer>> bom=Map.of(1,List.of(2),2,List.of(3),3,List.of(1));
    if(recursiveSupplierRisk(bom,1,16).stream().noneMatch(SupplierExposure::cycleDetected)) throw new AssertionError("BOM cycle not detected");
    var auth=new Authorization.Context("tenant-a",Set.of(1,2),Authorization.Role.ANALYST,false);
    if(Authorization.canReadWarehouse(auth,3)) throw new AssertionError("tenant isolation violated");
  }
  private Scenarios(){}
}
