package com.foundgine.samples.supplychain.advanced.authorization;

import com.foundgine.samples.supplychain.advanced.authorization.claims.Claims;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import java.util.*;

public final class Authorization {
  public enum Role { CUSTOMER, ANALYST, WAREHOUSE_OPERATOR, SUPPLY_CHAIN_MANAGER }
  public record Context(String tenantId,Set<Integer> allowedWarehouses,Role role,boolean readOnly){public Context{allowedWarehouses=Set.copyOf(allowedWarehouses);}}
  public static boolean canReadSupplierRisk(Context c){return c.role()==Role.ANALYST||c.role()==Role.SUPPLY_CHAIN_MANAGER;}
  public static boolean canWrite(Context c){return !c.readOnly()&&(c.role()==Role.WAREHOUSE_OPERATOR||c.role()==Role.SUPPLY_CHAIN_MANAGER);}
  public static boolean canReadWarehouse(Context c,int id){return c.allowedWarehouses().contains(id);}
  public static Context applyClaims(Context identity,Claims.ValidationResult claims){
    if(claims.isSpoofingAttempt()) throw new SecurityException("Client identity spoofing attempt rejected");
    Set<Integer> warehouses=new HashSet<>(identity.allowedWarehouses()); Object w=claims.typedAccepted().get("warehouse"); if(w instanceof Integer i) warehouses.retainAll(Set.of(i));
    boolean readOnly=identity.readOnly(); Object scope=claims.typedAccepted().get("scope"); if("read-only".equals(scope)) readOnly=true;
    return new Context(identity.tenantId(),warehouses,identity.role(),readOnly);
  }
  public static List<SupplyChainData> unused(){return List.of();}
  private Authorization(){}
}
