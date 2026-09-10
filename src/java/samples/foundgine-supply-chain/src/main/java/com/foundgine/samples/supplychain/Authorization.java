package com.foundgine.samples.supplychain;

import java.util.*;

public final class Authorization {
  public enum Role { CUSTOMER, ANALYST, WAREHOUSE_OPERATOR, SUPPLY_CHAIN_MANAGER }
  public record Context(String tenantId, Set<Integer> allowedWarehouses, Role role, boolean readOnly) {
    public Context { Objects.requireNonNull(tenantId); allowedWarehouses=Set.copyOf(allowedWarehouses); Objects.requireNonNull(role); }
  }
  public static boolean canReadSupplierRisk(Context c) { return c.role()==Role.ANALYST || c.role()==Role.SUPPLY_CHAIN_MANAGER; }
  public static boolean canWrite(Context c) { return !c.readOnly() && (c.role()==Role.WAREHOUSE_OPERATOR || c.role()==Role.SUPPLY_CHAIN_MANAGER); }
  public static boolean canReadWarehouse(Context c,int warehouseId) { return c.allowedWarehouses().contains(warehouseId); }
  private Authorization() {}
}
