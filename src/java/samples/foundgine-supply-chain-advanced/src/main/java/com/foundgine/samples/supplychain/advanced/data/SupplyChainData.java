package com.foundgine.samples.supplychain.advanced.data;

import com.foundgine.samples.supplychain.advanced.domain.Domain.*;
import java.math.BigDecimal; import java.time.LocalDate; import java.util.*;

/** Deterministic fixture matching the Advanced C# sample. */
public final class SupplyChainData {
  public final List<Product> products=new ArrayList<>(); public final List<ProductComponent> components=new ArrayList<>();
  public final List<Supplier> suppliers=new ArrayList<>(); public final List<SupplierCertification> certifications=new ArrayList<>();
  public final List<ComplianceIncident> incidents=new ArrayList<>(); public final List<Warehouse> warehouses=new ArrayList<>();
  public final List<BusinessUnit> businessUnits=new ArrayList<>(); public final List<PurchaseOrder> purchaseOrders=new ArrayList<>();
  public final List<PurchaseOrderLine> purchaseOrderLines=new ArrayList<>(); public final List<Shipment> shipments=new ArrayList<>();
  public final List<InventoryLot> inventory=new ArrayList<>(); public final List<Customer> customers=new ArrayList<>(); public final List<Order> orders=new ArrayList<>(); public final List<OrderItem> orderItems=new ArrayList<>(); public final List<OrderAllocation> orderAllocations=new ArrayList<>(); public final List<IdempotencyRecord> idempotency=new ArrayList<>(); public final List<CancellationIdempotencyRecord> cancellationIdempotency=new ArrayList<>(); public final List<CustomerOrder> customerOrders=new ArrayList<>();
  public final List<CustomerOrderLine> customerOrderLines=new ArrayList<>();

  public static SupplyChainData seed() {
    var d=new SupplyChainData();
    d.customers.addAll(List.of(new Customer(1,"Alice","tenant-a"),new Customer(2,"Bob","tenant-a")));
    d.businessUnits.addAll(List.of(new BusinessUnit(1,1,"Consumer Electronics"),new BusinessUnit(2,1,"Industrial Systems")));
    d.warehouses.addAll(List.of(new Warehouse(1,1,"Auckland DC","tenant-a"),new Warehouse(2,1,"Hamilton DC","tenant-a"),new Warehouse(3,2,"Restricted Rotorua DC","tenant-b")));
    d.suppliers.addAll(List.of(new Supplier(1,"Kiwi Components","NZ",bd("0.22"),"tenant-a"),new Supplier(2,"Pacific Semiconductors","TW",bd("0.87"),"tenant-a"),new Supplier(3,"Global Metals","US",bd("0.62"),"tenant-b")));
    d.products.addAll(List.of(new Product(1,"MOTOR-X","Industrial Motor","Motors",bd("30")),new Product(2,"CTRL-X","Motor Controller","Controls",bd("40")),new Product(3,"PCB-X","Controller PCB","Electronics",bd("80")),new Product(4,"CAP-X","High Reliability Capacitor","Electronics",bd("120")),new Product(5,"RES-X","Precision Resistor","Electronics",bd("100")),new Product(6,"PACK-X","Customer Assembly","Finished Goods",bd("25"))));
    d.components.addAll(List.of(pc(1,2,"1"),pc(2,3,"2"),pc(3,4,"8"),pc(3,5,"12"),pc(6,1,"2"),pc(5,2,"1")));
    d.certifications.addAll(List.of(new SupplierCertification(1,1,"ISO9001",date("2025-01-01"),date("2027-12-31")),new SupplierCertification(2,2,"ISO9001",date("2025-01-01"),date("2026-09-03")),new SupplierCertification(3,3,"ISO9001",date("2024-01-01"),date("2026-08-01"))));
    d.incidents.addAll(List.of(new ComplianceIncident(1,2,"Critical",date("2026-08-10"),"Capacity disruption"),new ComplianceIncident(2,3,"High",date("2026-07-04"),"Certification lapse")));
    d.purchaseOrders.addAll(List.of(new PurchaseOrder(100,2,1,PurchaseOrderStatus.OPEN,date("2026-08-01"),date("2026-08-29")),new PurchaseOrder(101,1,1,PurchaseOrderStatus.PARTIALLY_RECEIVED,date("2026-08-03"),date("2026-08-30")),new PurchaseOrder(102,3,3,PurchaseOrderStatus.CANCELLED,date("2026-07-01"),date("2026-08-20"))));
    d.purchaseOrderLines.addAll(List.of(new PurchaseOrderLine(1000,100,4,bd("1000"),bd("3.25")),new PurchaseOrderLine(1001,101,5,bd("600"),bd("0.42"))));
    d.shipments.addAll(List.of(new Shipment(500,100,date("2026-08-29"),null,ShipmentStatus.DELAYED,bd("1000")),new Shipment(501,101,date("2026-08-30"),null,ShipmentStatus.PARTIALLY_RECEIVED,bd("300"))));
    d.inventory.addAll(List.of(new InventoryLot(900,1,4,bd("70"),bd("20"),bd("10"),date("2026-08-01")),new InventoryLot(901,1,5,bd("90"),bd("75"),bd("0"),date("2026-08-02")),new InventoryLot(902,1,3,bd("25"),bd("10"),bd("5"),date("2026-08-04")),new InventoryLot(903,3,4,bd("5000"),bd("0"),bd("0"),date("2026-08-01"))));
    d.customerOrders.addAll(List.of(new CustomerOrder(700,1,date("2026-08-20"),"Open"),new CustomerOrder(701,2,date("2026-08-21"),"Open")));
    d.customerOrderLines.addAll(List.of(new CustomerOrderLine(7000,700,1,bd("40")),new CustomerOrderLine(7001,700,6,bd("20")),new CustomerOrderLine(7002,701,2,bd("100"))));
    return d;
  }
  private static ProductComponent pc(int p,int c,String q){return new ProductComponent(p,c,bd(q),null,null,"1",false,bd("0"),bd("0"));}
  private static BigDecimal bd(String s){return new BigDecimal(s);} private static LocalDate date(String s){return LocalDate.parse(s);}
  private SupplyChainData(){}
}
