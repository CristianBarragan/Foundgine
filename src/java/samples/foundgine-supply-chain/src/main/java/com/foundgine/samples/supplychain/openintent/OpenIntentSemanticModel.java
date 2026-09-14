package com.foundgine.samples.supplychain.openintent;

import java.util.*;

/** Semantic overlay vocabulary; physical metadata is supplied by the AOT/provider layer. */
public final class OpenIntentSemanticModel {
    private OpenIntentSemanticModel() {}
    private static final Map<String,List<String>> ALIASES = Map.ofEntries(
      Map.entry("Customer",List.of("client","buyer")), Map.entry("Order",List.of("purchase","customer order")),
      Map.entry("OrderItem",List.of("line item","order line")), Map.entry("Product",List.of("item","catalog item")),
      Map.entry("Supplier",List.of("vendor","seller")), Map.entry("Category",List.of("product category")),
      Map.entry("Inventory",List.of("stock","stock level")), Map.entry("Warehouse",List.of("site","distribution center")),
      Map.entry("Shipment",List.of("delivery","dispatch")), Map.entry("Carrier",List.of("shipper","delivery carrier")),
      Map.entry("PurchaseOrder",List.of("PO","purchase order")));
    public static Map<String,List<String>> aliases() { return ALIASES; }
}
