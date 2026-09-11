package com.foundgine.samples.supplychain.advanced.scenarios;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import java.math.BigDecimal; import java.util.*;

public final class Scenarios {
  public record SupplierRisk(int productId,int depth,boolean cycleDetected,List<Integer> path) {}
  public record FulfillmentRisk(int productId,String sku,BigDecimal demand,BigDecimal available,BigDecimal inbound,BigDecimal shortage,Set<Integer> suppliers) {}
  public static List<SupplierRisk> recursiveSupplierRisk(SupplyChainData d,int root,int maxDepth){
    Map<Integer,List<Integer>> graph=new HashMap<>(); for(var c:d.components) graph.computeIfAbsent(c.parentProductId(),x->new ArrayList<>()).add(c.componentProductId());
    List<SupplierRisk> out=new ArrayList<>(); walk(graph,root,0,maxDepth,new LinkedHashSet<>(),out); return List.copyOf(out);
  }
  private static void walk(Map<Integer,List<Integer>> g,int p,int depth,int max,LinkedHashSet<Integer> path,List<SupplierRisk> out){
    if(depth>max)return; if(!path.add(p)){out.add(new SupplierRisk(p,depth,true,List.copyOf(path)));return;} for(int child:g.getOrDefault(p,List.of())) walk(g,child,depth+1,max,path,out); path.remove(p);
  }
  public static List<FulfillmentRisk> fulfillment(SupplyChainData d,Authorization.Context c){
    List<FulfillmentRisk> out=new ArrayList<>();
    for(var line:d.customerOrderLines){var lots=d.inventory.stream().filter(i->i.productId()==line.productId()&&Authorization.canReadWarehouse(c,i.warehouseId())).toList(); BigDecimal available=lots.stream().map(i->i.onHand().subtract(i.reserved()).subtract(i.quarantined())).reduce(BigDecimal.ZERO,BigDecimal::add); BigDecimal demand=line.quantity(); BigDecimal inbound=d.purchaseOrderLines.stream().filter(x->x.productId()==line.productId()).map(PurchaseOrderLine::quantity).reduce(BigDecimal.ZERO,BigDecimal::add); BigDecimal shortage=demand.subtract(available.add(inbound)).max(BigDecimal.ZERO); var product=d.products.stream().filter(p->p.id()==line.productId()).findFirst(); if(product.isPresent()) out.add(new FulfillmentRisk(product.get().id(),product.get().sku(),demand,available,inbound,shortage,Set.of())); }
    return List.copyOf(out);
  }
  private Scenarios(){}
}
