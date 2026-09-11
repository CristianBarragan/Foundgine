package com.foundgine.samples.supplychain.advanced.mutation;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import java.math.BigDecimal; import java.nio.charset.StandardCharsets; import java.security.MessageDigest; import java.time.LocalDate; import java.util.*;

/** High-assurance in-memory PlaceOrder boundary matching the Advanced C# invariants. */
public final class PlaceOrderService {
  public record OrderLine(int productId,int quantity) {}
  public record Result(int orderId,boolean replay,BigDecimal total,String planFingerprint,String evidence) {}
  private final SupplyChainData data; private final Map<String,Object> locks=new HashMap<>(); private int nextOrderId=1,nextItemId=1;
  public PlaceOrderService(SupplyChainData data){this.data=Objects.requireNonNull(data);}
  public Result placeOrder(String actor,Authorization.Context auth,int customerId,List<OrderLine> lines,String key){
    Objects.requireNonNull(actor); Objects.requireNonNull(auth); Objects.requireNonNull(lines);
    if(lines.isEmpty())throw new IllegalArgumentException("At least one line is required.");
    if(lines.stream().anyMatch(x->x.quantity()<=0))throw new IllegalArgumentException("Quantity must be positive.");
    if(key==null||key.isBlank())throw new IllegalArgumentException("Idempotency key is required.");
    if(auth.readOnly()||(auth.role()!=Authorization.Role.CUSTOMER&&auth.role()!=Authorization.Role.SUPPLY_CHAIN_MANAGER))throw new SecurityException("Caller is not authorized to place orders.");
    synchronized(lockFor(key)){
      var prior=data.idempotency.stream().filter(x->x.key().equals(key)).findFirst().orElse(null);
      if(prior!=null)return new Result(prior.orderId(),true,data.orders.stream().filter(x->x.id()==prior.orderId()).map(Order::totalAmount).findFirst().orElse(BigDecimal.ZERO),planFingerprint(),evidence(actor,customerId,prior.orderId(),key));
      var customer=data.customers.stream().filter(x->x.id()==customerId).findFirst().orElseThrow(()->new IllegalArgumentException("Customer not found."));
      if(!customer.tenantId().equals(auth.tenantId()))throw new SecurityException("Customer belongs to another tenant.");
      if(auth.role()==Authorization.Role.CUSTOMER&&!actorCustomer(actor,customerId))throw new SecurityException("Customer ownership check failed.");
      var orders=new ArrayList<>(data.orders);var items=new ArrayList<>(data.orderItems);var allocations=new ArrayList<>(data.orderAllocations);var inventory=new ArrayList<>(data.inventory);var idem=new ArrayList<>(data.idempotency);
      try{
        var requested=new LinkedHashMap<Integer,Integer>();for(var l:lines)requested.merge(l.productId(),l.quantity(),Integer::sum);
        var resolved=new ArrayList<Resolved>();var total=BigDecimal.ZERO;
        for(var e:requested.entrySet()){
          var product=data.products.stream().filter(x->x.id()==e.getKey()).findFirst().orElseThrow(()->new IllegalArgumentException("Product "+e.getKey()+" not found."));int qty=e.getValue();
          var lot=data.inventory.stream().filter(x->x.productId()==product.id()).filter(x->auth.allowedWarehouses().contains(x.warehouseId())).filter(x->x.onHand().subtract(x.reserved()).subtract(x.quarantined()).compareTo(BigDecimal.valueOf(qty))>=0).max(Comparator.comparing(InventoryLot::onHand).thenComparing(InventoryLot::warehouseId)).orElseThrow(()->new IllegalStateException("Insufficient inventory for product "+product.id()+"."));
          resolved.add(new Resolved(product.id(),qty,product.unitPrice(),lot.warehouseId(),lot.id()));total=total.add(product.unitPrice().multiply(BigDecimal.valueOf(qty)));
        }
        int orderId=nextOrderId++;data.orders.add(new Order(orderId,customerId,"Pending",total,LocalDate.now()));
        for(var r:resolved){int itemId=nextItemId++;data.orderItems.add(new OrderItem(itemId,orderId,r.productId(),r.quantity(),r.unitPrice()));data.orderAllocations.add(new OrderAllocation(itemId,r.lotId(),r.quantity()));var lot=data.inventory.stream().filter(x->x.id()==r.lotId()).findFirst().orElseThrow();data.inventory.set(data.inventory.indexOf(lot),new InventoryLot(lot.id(),lot.warehouseId(),lot.productId(),lot.onHand().subtract(BigDecimal.valueOf(r.quantity)),lot.reserved(),lot.quarantined(),lot.receivedOn()));}
        data.idempotency.add(new IdempotencyRecord(key,actor,customerId,orderId));return new Result(orderId,false,total,planFingerprint(),evidence(actor,customerId,orderId,key));
      }catch(RuntimeException ex){data.orders.clear();data.orders.addAll(orders);data.orderItems.clear();data.orderItems.addAll(items);data.orderAllocations.clear();data.orderAllocations.addAll(allocations);data.inventory.clear();data.inventory.addAll(inventory);data.idempotency.clear();data.idempotency.addAll(idem);throw ex;}
    }
  }
  private boolean actorCustomer(String actor,int id){return actor.equalsIgnoreCase("customer"+id)||actor.equalsIgnoreCase("customer-"+id)||(actor.equalsIgnoreCase("alice")&&id==1)||(actor.equalsIgnoreCase("bob")&&id==2);}
  private Object lockFor(String key){return locks.computeIfAbsent(key,k->new Object());}
  private String planFingerprint(){return sha256("place_order|"+SupplyChainSemanticModel.MODEL.contractFingerprint()+"|Customer|Order|OrderItem|InventoryLot").substring(0,24);}
  private static String evidence(String actor,int customer,int order,String key){return sha256("place_order|"+actor+"|"+customer+"|"+order+"|"+key);}
  private static String sha256(String s){try{var md=MessageDigest.getInstance("SHA-256");var b=md.digest(s.getBytes(StandardCharsets.UTF_8));var o=new StringBuilder();for(byte x:b)o.append(String.format("%02x",x));return o.toString();}catch(Exception e){throw new IllegalStateException(e);}}
  private record Resolved(int productId,int quantity,BigDecimal unitPrice,int warehouseId,int lotId){}
}
