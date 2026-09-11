package com.foundgine.samples.supplychain.advanced;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.authorization.claims.Claims;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.scenarios.Scenarios;
import java.time.*; import java.util.*; import java.math.BigDecimal; import static org.junit.jupiter.api.Assertions.*; import org.junit.jupiter.api.Test;
public class AdvancedSupplyChainTest {
 @Test void seedMatchesAdvancedSecurityFixtures(){var d=SupplyChainData.seed(); assertEquals(3,d.warehouses.size()); assertEquals("tenant-b",d.warehouses.get(2).tenantId()); assertEquals(6,d.products.size());}
 @Test void bomCycleIsDetected(){var cycles=Scenarios.recursiveSupplierRisk(SupplyChainData.seed(),1,16); assertTrue(cycles.stream().anyMatch(Scenarios.SupplierRisk::cycleDetected));}
 @Test void tenantBWarehouseCannotBeReadByTenantA(){var c=new Authorization.Context("tenant-a",Set.of(1,2),Authorization.Role.ANALYST,false); assertFalse(Authorization.canReadWarehouse(c,3));}
 @Test void claimSpoofingFailsClosed(){var r=Claims.validate(Map.of("tenant","tenant-a","scope","full"),Instant.now()); assertTrue(r.isSpoofingAttempt()); assertEquals(Claims.Severity.SUSPICIOUS,r.spoofingSeverity()); assertTrue(r.accepted().isEmpty());}
 @Test void expiryRetractsEvidence(){var now=Instant.parse("2026-09-11T00:00:00Z"); var r=Claims.validate(Map.of("reason","approved operational reason","change_ticket","CHG-1234","not_after","2026-09-20T00:00:00Z"),now); assertTrue(r.accepted().isEmpty()); assertFalse(r.rejected().isEmpty());}
 @Test void warehouseClaimNarrowsExistingAuthority(){var identity=new Authorization.Context("tenant-a",Set.of(1,2),Authorization.Role.ANALYST,false); var r=Claims.validate(Map.of("warehouse","2"),Instant.now()); var effective=Authorization.applyClaims(identity,r); assertEquals(Set.of(2),effective.allowedWarehouses());}
 @Test void placeOrderIsAtomicAndIdempotent(){
   var d=SupplyChainData.seed(); var service=new com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService(d);
   var auth=new Authorization.Context("tenant-a",Set.of(1,2),Authorization.Role.CUSTOMER,false);
   var before=d.inventory.stream().filter(x->x.id()==900).findFirst().orElseThrow().onHand();
   var first=service.placeOrder("alice",auth,1,List.of(new com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService.OrderLine(4,5)),"idem-1");
   var after=d.inventory.stream().filter(x->x.id()==900).findFirst().orElseThrow().onHand();
   assertFalse(first.replay()); assertEquals(0,before.subtract(after).compareTo(BigDecimal.valueOf(5))); assertEquals(1,d.orders.size());
   var replay=service.placeOrder("alice",auth,1,List.of(new com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService.OrderLine(4,5)),"idem-1");
   assertTrue(replay.replay()); assertEquals(first.orderId(),replay.orderId()); assertEquals(1,d.orders.size()); assertEquals(after,d.inventory.stream().filter(x->x.id()==900).findFirst().orElseThrow().onHand());
 }
 @Test void placeOrderFailsClosedForRestrictedWarehouse(){
   var d=SupplyChainData.seed(); var service=new com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService(d);
   var auth=new Authorization.Context("tenant-a",Set.of(1,2),Authorization.Role.CUSTOMER,false);
   assertThrows(IllegalStateException.class,()->service.placeOrder("alice",auth,1,List.of(new com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService.OrderLine(4,5000)),"idem-restricted"));
   assertTrue(d.orders.isEmpty()); assertTrue(d.idempotency.isEmpty());
 }
 @Test void placeOrderRejectsReadOnlyClaims(){
   var d=SupplyChainData.seed(); var service=new com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService(d);
   var auth=new Authorization.Context("tenant-a",Set.of(1,2),Authorization.Role.CUSTOMER,true);
   assertThrows(SecurityException.class,()->service.placeOrder("alice",auth,1,List.of(new com.foundgine.samples.supplychain.advanced.mutation.PlaceOrderService.OrderLine(4,1)),"idem-readonly"));
 }

}

