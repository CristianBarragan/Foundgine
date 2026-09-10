package com.foundgine.samples.supplychain.advanced.authorization;

import com.foundgine.samples.supplychain.advanced.domain.Domain.WarehouseId;
import java.util.*;

/** Application-owned authorization context: transport claims never create authority. */
public final class SupplyChainAuthorization {
    public enum Role { CUSTOMER, ANALYST, WAREHOUSE_OPERATOR, SUPPLY_CHAIN_MANAGER }
    public record Context(String tenantId, Set<WarehouseId> allowedWarehouses, Role role, boolean readOnly) {
        public Context { Objects.requireNonNull(tenantId); allowedWarehouses=Set.copyOf(allowedWarehouses); Objects.requireNonNull(role); }
    }
    public static boolean canReadSupplierRisk(Context c) { return c.role()==Role.ANALYST || c.role()==Role.SUPPLY_CHAIN_MANAGER; }
    public static boolean canWritePurchasing(Context c) { return !c.readOnly() && (c.role()==Role.WAREHOUSE_OPERATOR || c.role()==Role.SUPPLY_CHAIN_MANAGER); }
    public static boolean canReadWarehouse(Context c, WarehouseId id) { return c.allowedWarehouses().contains(id); }
    public static Context tenantAAnalyst() { return new Context("tenant-a",Set.of(new WarehouseId(1),new WarehouseId(2)),Role.ANALYST,true); }
    private SupplyChainAuthorization() {}
}
