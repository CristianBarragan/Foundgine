package com.foundgine.samples.supplychain.advanced;

import com.foundgine.samples.supplychain.advanced.application.SupplyChainScenarios;
import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import com.foundgine.samples.supplychain.advanced.semantic.SupplyChainSemanticModel;
import org.junit.jupiter.api.Test;
import java.time.LocalDate;
import static org.junit.jupiter.api.Assertions.*;

class SupplyChainAdvancedPortTest {
    @Test void tenantBWarehouseIsNotAuthorized() {
        var auth=SupplyChainAuthorization.tenantAAnalyst();
        assertFalse(SupplyChainAuthorization.canReadWarehouse(auth,new WarehouseId(3)));
    }
    @Test void bomCycleIsDetectedAndBounded() {
        var rows=SupplyChainScenarios.recursiveSupplierRisk(SupplyChainData.seed(),new ProductId(1),SupplyChainAuthorization.tenantAAnalyst());
        assertTrue(rows.stream().anyMatch(SupplyChainScenarios.SupplierExposure::cycleDetected));
    }
    @Test void expiredCertificationFixtureExists() {
        assertTrue(SupplyChainData.seed().certifications.stream().anyMatch(x->x.supplierId().equals(new SupplierId(3)) && x.validTo().isBefore(LocalDate.of(2026,8,27))));
    }
    @Test void semanticContractBuildsAndContainsAdvancedTraversalsPrerequisites() {
        var model=SupplyChainSemanticModel.build().freeze();
        assertNotNull(model.contractFingerprint());
        assertNotNull(model.get(SupplyChainSemanticModel.PRODUCT));
        assertNotNull(model.get(SupplyChainSemanticModel.PURCHASE_ORDER));
        assertTrue(model.get(SupplyChainSemanticModel.PRODUCT).relationships().stream().anyMatch(x->x.name().equals("components")));
    }
}
