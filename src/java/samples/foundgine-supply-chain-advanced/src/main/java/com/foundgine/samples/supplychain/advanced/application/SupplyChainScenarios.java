package com.foundgine.samples.supplychain.advanced.application;

import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization.Context;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.*;

/** High-assurance application scenarios ported from the C# advanced sample. */
public final class SupplyChainScenarios {
    public static final int RECURSIVE_BOM_MAX_DEPTH = 16;
    public record SupplierExposure(ProductId productId, SupplierId supplierId, int depth, boolean cycleDetected) {}
    public record FulfillmentRisk(ProductId productId,String sku,BigDecimal demand,BigDecimal available,BigDecimal projectedInbound,BigDecimal projectedShortage,List<SupplierId> suppliers) {}

    public static List<SupplierExposure> recursiveSupplierRisk(SupplyChainData d, ProductId root, Context auth) {
        var result=new ArrayList<SupplierExposure>(); var visited=new HashSet<ProductId>();
        walk(d,root,0,new HashSet<>(),visited,result,auth); return List.copyOf(result);
    }
    private static void walk(SupplyChainData d, ProductId product,int depth,Set<ProductId> path,Set<ProductId> visited,List<SupplierExposure> result,Context auth) {
        if(depth>RECURSIVE_BOM_MAX_DEPTH)return;
        if(!path.add(product)){result.add(new SupplierExposure(product,new SupplierId(0),depth,true));return;}
        if(!visited.add(product)){path.remove(product);return;}
        d.components.stream().filter(x->x.parentProductId().equals(product)).map(ProductComponent::componentProductId).distinct().forEach(child -> {
            d.purchaseOrderLines.stream().filter(x->x.productId().equals(child)).map(x->d.purchaseOrders.stream().filter(p->p.id().equals(x.purchaseOrderId())).map(PurchaseOrder::supplierId).findFirst().orElse(null)).filter(Objects::nonNull).distinct().forEach(supplier ->
                d.suppliers.stream().filter(s->s.id().equals(supplier) && s.tenantId().equals(auth.tenantId())).findFirst().ifPresent(s -> result.add(new SupplierExposure(child,s.id(),depth+1,false))));
            walk(d,child,depth+1,path,visited,result,auth);
        });
        path.remove(product);
    }

    public static List<FulfillmentRisk> fulfillmentPlanning(SupplyChainData d, LocalDate asOf, Context auth) {
        var demand=new LinkedHashMap<ProductId,BigDecimal>();
        d.customerOrderLines.forEach(line -> d.customerOrders.stream().filter(o->o.id().equals(line.customerOrderId()) && "Open".equalsIgnoreCase(o.status())).findFirst().ifPresent(o -> demand.merge(line.productId(),line.quantity(),BigDecimal::add)));
        var output=new ArrayList<FulfillmentRisk>();
        for(var e:demand.entrySet()) {
            var productId=e.getKey(); var qty=e.getValue();
            var available=d.inventory.stream().filter(i->i.productId().equals(productId) && auth.allowedWarehouses().contains(i.warehouseId()))
                .map(i->i.onHand().subtract(i.reserved()).subtract(i.quarantined()).max(BigDecimal.ZERO)).reduce(BigDecimal.ZERO,BigDecimal::add);
            var inbound=d.purchaseOrderLines.stream().filter(l->l.productId().equals(productId)).flatMap(l->d.purchaseOrders.stream().filter(p->p.id().equals(l.purchaseOrderId()) &&
                (p.status()==PurchaseOrderStatus.OPEN || p.status()==PurchaseOrderStatus.PARTIALLY_RECEIVED) && auth.allowedWarehouses().contains(p.warehouseId())))
                .flatMap(p->d.shipments.stream().filter(s->s.purchaseOrderId().equals(p.id()) && (s.status()==ShipmentStatus.IN_TRANSIT || s.status()==ShipmentStatus.DELAYED || s.status()==ShipmentStatus.PARTIALLY_RECEIVED) && !s.expectedArrival().isAfter(asOf.plusDays(14))))
                .map(Shipment::quantity).reduce(BigDecimal.ZERO,BigDecimal::add);
            var suppliers=d.purchaseOrderLines.stream().filter(l->l.productId().equals(productId)).map(l->d.purchaseOrders.stream().filter(p->p.id().equals(l.purchaseOrderId())).map(PurchaseOrder::supplierId).findFirst().orElse(null)).filter(Objects::nonNull).distinct()
                .filter(id->d.suppliers.stream().anyMatch(s->s.id().equals(id)&&s.tenantId().equals(auth.tenantId()))).toList();
            var shortage=qty.subtract(available).subtract(inbound).max(BigDecimal.ZERO);
            if(shortage.signum()>0) { var product=d.products.stream().filter(p->p.id().equals(productId)).findFirst().orElseThrow(); output.add(new FulfillmentRisk(productId,product.sku(),qty,available,inbound,shortage,suppliers)); }
        }
        return output.stream().sorted(Comparator.comparing(FulfillmentRisk::projectedShortage).reversed().thenComparing(x->x.productId().value())).limit(20).toList();
    }

    public static void assertAdversarialInvariants(SupplyChainData d, Context auth) {
        if(d.inventory.stream().anyMatch(i->i.warehouseId().value()==3 && !auth.allowedWarehouses().contains(i.warehouseId()))) System.out.println("PASS tenant isolation: restricted warehouse is present but excluded by authorization.");
        if(recursiveSupplierRisk(d,new ProductId(1),auth).stream().noneMatch(SupplierExposure::cycleDetected)) throw new IllegalStateException("Expected BOM cycle to be detected.");
        System.out.println("PASS recursive safety: BOM cycle detected and traversal terminated.");
        if(d.certifications.stream().filter(c->c.validTo().isBefore(LocalDate.of(2026,8,27))).noneMatch(c->c.supplierId().equals(new SupplierId(3)))) throw new IllegalStateException("Expected expired supplier certification fixture.");
        System.out.println("PASS temporal security: expired supplier certification fixture detected.");
        if(auth.allowedWarehouses().contains(new WarehouseId(3))) throw new IllegalStateException("Adversarial authorization context accidentally exposes tenant-b warehouse.");
        System.out.println("PASS authorization: caller cannot access tenant-b warehouse.");
    }
    private SupplyChainScenarios() {}
}
