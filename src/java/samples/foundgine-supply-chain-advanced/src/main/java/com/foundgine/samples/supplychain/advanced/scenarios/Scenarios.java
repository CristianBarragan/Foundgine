package com.foundgine.samples.supplychain.advanced.scenarios;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import java.math.BigDecimal; import java.util.*;

public final class Scenarios {
  public record SupplierRisk(int productId,Integer supplierId,int depth,boolean cycleDetected,List<Integer> path) {}
  public record FulfillmentRisk(int productId,String sku,BigDecimal demand,BigDecimal available,BigDecimal inbound,BigDecimal shortage,Set<Integer> suppliers) {}
  /**
   * Ports C#'s {@code SupplyChainScenarios.RecursiveSupplierRisk(SupplyChainData, ProductId, AuthorizationContext)}.
   * The Java version previously had no {@code AuthorizationContext} parameter and performed no tenant
   * filtering at all -- every supplier exposure was reported regardless of which tenant it belonged to,
   * and {@code SupplierRisk} did not even carry a supplier id. This restores parity: a supplier exposure
   * is only recorded when {@code Supplier.tenantId} matches {@code auth.tenantId()}, exactly mirroring the
   * C# {@code d.Suppliers.FirstOrDefault(s => s.Id == supplier)?.TenantId == auth.TenantId} check.
   */
  public static List<SupplierRisk> recursiveSupplierRisk(SupplyChainData d,int root,int maxDepth,Authorization.Context auth){
    Map<Integer,List<Integer>> graph=new HashMap<>(); for(var c:d.components) graph.computeIfAbsent(c.parentProductId(),x->new ArrayList<>()).add(c.componentProductId());
    List<SupplierRisk> out=new ArrayList<>(); walk(d,graph,root,0,maxDepth,new LinkedHashSet<>(),out,auth); return List.copyOf(out);
  }
  private static void walk(SupplyChainData d,Map<Integer,List<Integer>> g,int p,int depth,int max,LinkedHashSet<Integer> path,List<SupplierRisk> out,Authorization.Context auth){
    if(depth>max)return; if(!path.add(p)){out.add(new SupplierRisk(p,null,depth,true,List.copyOf(path)));return;}
    for(int child:g.getOrDefault(p,List.of())){
      var supplierIds=d.purchaseOrderLines.stream().filter(l->l.productId()==child)
          .map(l->d.purchaseOrders.stream().filter(po->po.id()==l.purchaseOrderId()).findFirst().map(PurchaseOrder::supplierId).orElse(null))
          .filter(Objects::nonNull).distinct().toList();
      for(int supplierId:supplierIds){
        boolean sameTenant=d.suppliers.stream().filter(s->s.id()==supplierId).findFirst()
            .map(s->s.tenantId().equals(auth.tenantId())).orElse(false);
        if(sameTenant) out.add(new SupplierRisk(child,supplierId,depth+1,false,List.copyOf(path)));
      }
      walk(d,g,child,depth+1,max,path,out,auth);
    }
    path.remove(p);
  }
  public static List<FulfillmentRisk> fulfillment(SupplyChainData d,Authorization.Context c){
    List<FulfillmentRisk> out=new ArrayList<>();
    for(var line:d.customerOrderLines){var lots=d.inventory.stream().filter(i->i.productId()==line.productId()&&Authorization.canReadWarehouse(c,i.warehouseId())).toList(); BigDecimal available=lots.stream().map(i->i.onHand().subtract(i.reserved()).subtract(i.quarantined())).reduce(BigDecimal.ZERO,BigDecimal::add); BigDecimal demand=line.quantity(); BigDecimal inbound=d.purchaseOrderLines.stream().filter(x->x.productId()==line.productId()).map(PurchaseOrderLine::quantity).reduce(BigDecimal.ZERO,BigDecimal::add); BigDecimal shortage=demand.subtract(available.add(inbound)).max(BigDecimal.ZERO); var product=d.products.stream().filter(p->p.id()==line.productId()).findFirst(); if(product.isPresent()) out.add(new FulfillmentRisk(product.get().id(),product.get().sku(),demand,available,inbound,shortage,Set.of())); }
    return List.copyOf(out);
  }
  private Scenarios(){}
}
