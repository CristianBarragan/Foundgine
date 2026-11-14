#!/usr/bin/env python3
"""Schema spec of the Starter (mirrors SupplyChainStorage.cs + PersistenceConfigurations.cs).
Usage: python3 tools/gen_migration.py Migrations"""
import sys
from efgen import *

M_ = "Foundgine.SupplyChain.Domain.Models."
D_ = "Foundgine.SupplyChain.Domain."
g = Gen("Foundgine.SupplyChain.Migrations", "FoundgineDbContext", "20261115000000", "InitialSupplyChain",
        "Initial supply-chain schema for PostgreSQL. Generated from tools/gen_migration.py.")

g.ent(M_ + "Customer", "customers",
      [ID("Id", "customer_id"), S("FirstName", "first_name", 100), S("LastName", "last_name", 100), S("Email", "email", 320)],
      ["Id"], "customers_pkey", idx=[("ux_customers_email", ["Email"], True, None, None)])
g.ent(M_ + "Supplier", "suppliers",
      [ID("Id", "supplier_id"), S("Name", "supplier_name", 100), S("Email", "email", 320)], ["Id"], "suppliers_pkey")
g.ent(M_ + "Category", "categories",
      [ID("Id", "category_id"), S("Name", "category_name", 100)], ["Id"], "categories_pkey")
g.ent(M_ + "Warehouse", "warehouses",
      [ID("Id", "warehouse_id"), S("Name", "warehouse_name", 100), S("Location", "location", 200)], ["Id"], "warehouses_pkey")
g.ent(M_ + "Carrier", "carriers",
      [ID("Id", "carrier_id"), S("Name", "carrier_name", 100)], ["Id"], "carriers_pkey")
g.ent(M_ + "CatalogProduct", "products",
      [ID("Id", "product_id"), S("Name", "product_name", 200), S("Sku", "sku", 64), M("UnitPrice", "unit_price", 12, 2),
       I("SupplierId", "supplier_id"), I("CategoryId", "category_id")],
      ["Id"], "products_pkey", idx=[("ux_products_sku", ["Sku"], True, None, None)],
      checks=[("products_unit_price_check", "unit_price >= 0")],
      fks=[("SupplierId", M_ + "Supplier", "products_supplier_id_fkey"), ("CategoryId", M_ + "Category", "products_category_id_fkey")])
g.ent(M_ + "InventoryPosition", "inventory",
      [ID("Id", "inventory_id"), I("WarehouseId", "warehouse_id"), I("ProductId", "product_id"),
       I("QuantityOnHand", "quantity_on_hand"), I("ReorderLevel", "reorder_level"),
       D("LastUpdated", "last_updated", default_sql="CURRENT_TIMESTAMP")],
      ["Id"], "inventory_pkey",
      idx=[("ux_inventory_warehouse_product", ["WarehouseId", "ProductId"], True, None, None), ("ix_inventory_product", ["ProductId"], False, None, None)],
      checks=[("inventory_quantity_check", "quantity_on_hand >= 0"), ("inventory_reorder_check", "reorder_level >= 0")],
      fks=[("WarehouseId", M_ + "Warehouse", "inventory_warehouse_id_fkey"), ("ProductId", M_ + "CatalogProduct", "inventory_product_id_fkey")])
g.ent(M_ + "SalesOrder", "orders",
      [ID("Id", "order_id"), I("CustomerId", "customer_id"), S("Status", "status", 20), M("TotalAmount", "total_amount", 14, 2)],
      ["Id"], "orders_pkey", idx=[("ix_orders_customer", ["CustomerId"], False, None, None)],
      checks=[("orders_status_check", "status IN ('Pending','Cancelled','Shipped','Delivered')")],
      fks=[("CustomerId", M_ + "Customer", "orders_customer_id_fkey")])
g.ent(M_ + "SalesOrderLine", "order_items",
      [ID("Id", "order_item_id"), I("OrderId", "order_id"), I("ProductId", "product_id"), I("Quantity", "quantity"), M("UnitPrice", "unit_price", 12, 2)],
      ["Id"], "order_items_pkey", idx=[("ix_order_items_order", ["OrderId"], False, None, None)],
      checks=[("order_items_quantity_check", "quantity > 0"), ("order_items_unit_price_check", "unit_price >= 0")],
      fks=[("OrderId", M_ + "SalesOrder", "order_items_order_id_fkey"), ("ProductId", M_ + "CatalogProduct", "order_items_product_id_fkey")])
g.ent(M_ + "Shipment", "shipments",
      [ID("Id", "shipment_id"), I("OrderId", "order_id"), I("CarrierId", "carrier_id"), I("WarehouseId", "warehouse_id"),
       S("TrackingNumber", "tracking_number", 100), S("Status", "shipping_status", 30)],
      ["Id"], "shipments_pkey", idx=[("ix_shipments_order", ["OrderId"], False, None, None)],
      checks=[("shipments_status_check", "shipping_status IN ('In Transit','Out for Delivery','Delivered','Delayed')")],
      fks=[("OrderId", M_ + "SalesOrder", "shipments_order_id_fkey"), ("CarrierId", M_ + "Carrier", "shipments_carrier_id_fkey"),
           ("WarehouseId", M_ + "Warehouse", "shipments_warehouse_id_fkey")])
g.ent(D_ + "OrderAllocation", "order_allocations",
      [I("OrderItemId", "order_item_id"), I("WarehouseId", "warehouse_id"), I("Quantity", "quantity")],
      ["OrderItemId", "WarehouseId"], "order_allocations_pkey",
      checks=[("order_allocations_quantity_check", "quantity > 0")],
      fks=[("OrderItemId", M_ + "SalesOrderLine", "order_allocations_order_item_id_fkey"), ("WarehouseId", M_ + "Warehouse", "order_allocations_warehouse_id_fkey")])
g.ent(D_ + "IdempotencyRecord", "supply_chain_idempotency",
      [P("IdempotencyKey", "string", "idempotency_key", vc(200), maxlen=200, never=True), I("ActorId", "actor_id"),
       S("Operation", "operation", 50), I("OrderId", "order_id"), D("CreatedAt", "created_at", default_sql="CURRENT_TIMESTAMP")],
      ["IdempotencyKey"], "supply_chain_idempotency_pkey",
      fks=[("OrderId", M_ + "SalesOrder", "supply_chain_idempotency_order_id_fkey")])

g.write(sys.argv[1] if len(sys.argv) > 1 else "Migrations")
