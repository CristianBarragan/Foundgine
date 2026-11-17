package com.foundgine.benchmark;
import org.springframework.graphql.data.method.annotation.*; import org.springframework.security.core.Authentication; import org.springframework.stereotype.Controller; import jakarta.persistence.EntityManager; import com.foundgine.samples.supplychain.Authorization; import com.foundgine.samples.supplychain.jpa.SupplyChainJpa; import com.foundgine.core.execution.ExecutionRow; import java.util.*;
@Controller public class SupplyChainGraphQl implements AutoCloseable {
 private final SupplyChainJpa db=new SupplyChainJpa();
 @QueryMapping public List<Map<String,Object>> product(@Argument Integer first,Authentication a){return read(a,"Product",List.of("id","sku","name","category","safetyStock"),first);}
 @QueryMapping public List<Map<String,Object>> supplier(@Argument Integer first,Authentication a){return read(a,"Supplier",List.of("id","name","country","riskScore","tenantId"),first);}
 @QueryMapping public List<Map<String,Object>> warehouse(@Argument Integer first,Authentication a){return read(a,"Warehouse",List.of("id","businessUnitId","name","tenantId"),first);}
 private List<Map<String,Object>> read(Authentication a,String entity,List<String> fields,Integer first){Caller c=Caller.from(a);var caller=new Authorization.Context(c.tenantId(),Set.of(),role(c.role()),!c.canWrite());var em=db.openEntityManager(); try { var reader=db.orm().reader(em); var rows=reader.read(caller,entity,q->{q.select(fields);q.take(Math.max(1,Math.min(first==null?50:first,500)));}); return rows.stream().map(ExecutionRow::values).toList(); } finally { em.close(); }}
 @MutationMapping public Map<String,Object> updateWarehouse(@Argument Map<String,Object> input,@Argument Map<String,Object> where,Authentication a){
  Caller c=Caller.from(a); if(!c.canWrite()) throw new SecurityException("warehouse write role required");
  int id=((Number)where.get("id")).intValue(); String name=(String)input.get("name");
  var em=db.openEntityManager(); try {
   var gateway=db.entities(new Authorization.Context(c.tenantId(),Set.of(),role(c.role()),false),em).entity("Warehouse");
   var owned=gateway.read(Map.of("id",id,"tenantId",c.tenantId()),List.of("id","businessUnitId","name","tenantId"));
   if(owned.isEmpty()) return null;
   gateway.update(id,Map.of("name",name));
   return gateway.read(Map.of("id",id,"tenantId",c.tenantId()),List.of("id","businessUnitId","name","tenantId")).stream().findFirst().orElse(null);
  } finally { em.close(); }
 }
 private static Authorization.Role role(String r){return switch(r){case "SUPPLY_CHAIN_MANAGER"->Authorization.Role.SUPPLY_CHAIN_MANAGER;case "WAREHOUSE_OPERATOR"->Authorization.Role.WAREHOUSE_OPERATOR;case "ANALYST"->Authorization.Role.ANALYST;default->Authorization.Role.CUSTOMER;};}
 public void close(){db.close();}
}
