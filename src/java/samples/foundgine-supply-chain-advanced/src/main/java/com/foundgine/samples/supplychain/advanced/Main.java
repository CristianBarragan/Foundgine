package com.foundgine.samples.supplychain.advanced;

import com.foundgine.samples.supplychain.advanced.application.SupplyChainScenarios;
import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.semantic.SupplyChainSemanticModel;

/** Executable Java entry point for the advanced SupplyChain port. */
public final class Main {
    public static void main(String[] args) {
        var data=SupplyChainData.seed();
        var auth=SupplyChainAuthorization.tenantAAnalyst();
        var model=SupplyChainSemanticModel.build().freeze();
        System.out.println("Foundgine SupplyChain Advanced — Java port");
        System.out.println("Semantic contract: " + model.contractFingerprint());
        System.out.println("Entities: " + model.entities().size());
        System.out.println("Fulfillment risks: " + SupplyChainScenarios.fulfillmentPlanning(data,java.time.LocalDate.of(2026,8,27),auth).size());
        SupplyChainScenarios.assertAdversarialInvariants(data,auth);
    }
    private Main() {}
}
