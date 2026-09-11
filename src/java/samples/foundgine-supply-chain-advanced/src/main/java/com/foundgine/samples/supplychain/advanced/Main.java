package com.foundgine.samples.supplychain.advanced;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.authorization.claims.Claims;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.scenarios.Scenarios;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import java.time.Instant; import java.util.*;

public final class Main {
  public static void main(String[] args){
    var data=SupplyChainData.seed(); var identity=new Authorization.Context("tenant-a",Set.of(1,2),Authorization.Role.SUPPLY_CHAIN_MANAGER,false);
    var claims=Claims.validate(Map.of("warehouse","1","scope","read-only","max_rows","100"),Instant.now()); var effective=Authorization.applyClaims(identity,claims);
    var cycles=Scenarios.recursiveSupplierRisk(data,1,16); System.out.println("Foundgine Advanced Supply Chain Java sample"); System.out.println("accepted claims="+claims.accepted().keySet()+", effective warehouses="+effective.allowedWarehouses()); System.out.println("BOM cycles detected="+cycles.stream().filter(Scenarios.SupplierRisk::cycleDetected).count()); System.out.println("fulfillment rows="+Scenarios.fulfillment(data,effective).size());
    var orderAuth=new Authorization.Context("tenant-a",Set.of(1,2),Authorization.Role.SUPPLY_CHAIN_MANAGER,false);
    var pipeline=new com.foundgine.samples.supplychain.advanced.mutation.AdvancedMutationPipeline(data,orderAuth);
    var placed=pipeline.placeOrder("alice",1,4,2,"demo-place-order-1").toCompletableFuture().join();
    var replay=pipeline.placeOrder("alice",1,4,2,"demo-place-order-1").toCompletableFuture().join();
    System.out.println("semantic entities="+SupplyChainSemanticModel.MODEL.entities().size()+", contract="+SupplyChainSemanticModel.MODEL.contractFingerprint());
    System.out.println("runtime place_order plan="+placed.planFingerprint()+", result="+placed.resultFingerprint()+", replay-result="+replay.resultFingerprint());
    var orderId=placed.result().results().getFirst().returnedValues().values().stream().findFirst().orElseThrow();
    var cancelled=pipeline.cancelOrder("alice",((Number)orderId).intValue(),"demo-cancel-order-1").toCompletableFuture().join();
    var cancelReplay=pipeline.cancelOrder("alice",((Number)orderId).intValue(),"demo-cancel-order-1").toCompletableFuture().join();
    System.out.println("runtime cancel_order plan="+cancelled.planFingerprint()+", result="+cancelled.resultFingerprint()+", replay-result="+cancelReplay.resultFingerprint());
  }
}
