package com.foundgine.samples.supplychain.advanced.model;

import com.foundgine.core.semantic.configuration.*;
import com.foundgine.samples.supplychain.advanced.domain.Domain.*;

/**
 * Storage side: tables, keys and indexes for each persistence type. Warehouse, InventoryLot and Product use the real
 * table names of db/migration/V1__supply_chain_advanced.sql (the entities the JPA path maps).
 */
final class PersistenceConfigurations {
    private PersistenceConfigurations() {}

    static final class CompanyStorage extends FoundginePersistenceConfiguration<Company> {
        @Override
        public void configure(FoundginePersistenceBuilder<Company> model) {
            model.toTable("Company");
            model.hasKey(Company::id);
        }
    }

    static final class BusinessUnitStorage extends FoundginePersistenceConfiguration<BusinessUnit> {
        @Override
        public void configure(FoundginePersistenceBuilder<BusinessUnit> model) {
            model.toTable("BusinessUnit");
            model.hasKey(BusinessUnit::id);
            model.hasIndex(BusinessUnit::companyId);
        }
    }

    static final class WarehouseStorage extends FoundginePersistenceConfiguration<Warehouse> {
        @Override
        public void configure(FoundginePersistenceBuilder<Warehouse> model) {
            model.toTable("warehouses");
            model.hasKey(Warehouse::id);
            model.property(Warehouse::id).hasColumnName("warehouse_id").hasColumnType("integer").isRequired();
            model.property(Warehouse::name).hasColumnName("warehouse_name").hasMaxLength(120).isRequired();
            model.property(Warehouse::businessUnitId).hasColumnName("business_unit_id").hasColumnType("integer").isRequired();
            model.property(Warehouse::tenantId).hasColumnName("tenant_id").hasMaxLength(80).isRequired();
        }
    }

    static final class SupplierStorage extends FoundginePersistenceConfiguration<Supplier> {
        @Override
        public void configure(FoundginePersistenceBuilder<Supplier> model) {
            model.toTable("Supplier");
            model.hasKey(Supplier::id);
        }
    }

    static final class SupplierSiteStorage extends FoundginePersistenceConfiguration<SupplierSite> {
        @Override
        public void configure(FoundginePersistenceBuilder<SupplierSite> model) {
            model.toTable("SupplierSite");
            model.hasKey(SupplierSite::id);
        }
    }

    static final class SupplierCertificationStorage extends FoundginePersistenceConfiguration<SupplierCertification> {
        @Override
        public void configure(FoundginePersistenceBuilder<SupplierCertification> model) {
            model.toTable("SupplierCertification");
            model.hasKey(SupplierCertification::id);
        }
    }

    static final class ProductStorage extends FoundginePersistenceConfiguration<Product> {
        @Override
        public void configure(FoundginePersistenceBuilder<Product> model) {
            model.toTable("products");
            model.hasKey(Product::id);
            model.property(Product::id).hasColumnName("product_id").hasColumnType("integer").isRequired();
            model.property(Product::sku).hasColumnName("sku").hasMaxLength(50).isRequired();
            model.property(Product::name).hasColumnName("product_name").hasMaxLength(120).isRequired();
            model.property(Product::category).hasColumnName("category").hasMaxLength(80).isRequired();
            model.property(Product::safetyStock).hasColumnName("safety_stock").hasPrecision(18, 2).isRequired();
            model.property(Product::unitPrice).hasColumnName("unit_price").hasPrecision(18, 2).isRequired();
            model.hasUniqueIndex("products_sku_key", Product::sku);
            model.hasCheck("ck_products_unit_price", "unit_price >= 0");
        }
    }

    static final class ProductComponentStorage extends FoundginePersistenceConfiguration<ProductComponent> {
        @Override
        public void configure(FoundginePersistenceBuilder<ProductComponent> model) {
            model.toTable("ProductComponent");
            model.hasKey(ProductComponent::parentProductId);
        }
    }

    static final class PurchaseOrderStorage extends FoundginePersistenceConfiguration<PurchaseOrder> {
        @Override
        public void configure(FoundginePersistenceBuilder<PurchaseOrder> model) {
            model.toTable("PurchaseOrder");
            model.hasKey(PurchaseOrder::id);
        }
    }

    static final class PurchaseOrderLineStorage extends FoundginePersistenceConfiguration<PurchaseOrderLine> {
        @Override
        public void configure(FoundginePersistenceBuilder<PurchaseOrderLine> model) {
            model.toTable("PurchaseOrderLine");
            model.hasKey(PurchaseOrderLine::id);
        }
    }

    static final class ShipmentStorage extends FoundginePersistenceConfiguration<Shipment> {
        @Override
        public void configure(FoundginePersistenceBuilder<Shipment> model) {
            model.toTable("Shipment");
            model.hasKey(Shipment::id);
        }
    }

    static final class InventoryLotStorage extends FoundginePersistenceConfiguration<InventoryLot> {
        @Override
        public void configure(FoundginePersistenceBuilder<InventoryLot> model) {
            model.toTable("inventory_lots");
            model.hasKey(InventoryLot::id);
            model.property(InventoryLot::id).hasColumnName("lot_id").hasColumnType("integer").isRequired();
            model.property(InventoryLot::warehouseId).hasColumnName("warehouse_id").hasColumnType("integer").isRequired();
            model.property(InventoryLot::productId).hasColumnName("product_id").hasColumnType("integer").isRequired();
            model.property(InventoryLot::onHand).hasColumnName("on_hand").hasPrecision(18, 2).isRequired();
            model.property(InventoryLot::reserved).hasColumnName("reserved").hasPrecision(18, 2).hasDefault("0").isRequired();
            model.property(InventoryLot::quarantined).hasColumnName("quarantined").hasPrecision(18, 2).hasDefault("0").isRequired();
            model.property(InventoryLot::receivedOn).hasColumnName("received_on").hasColumnType("date").isRequired();
            model.hasIndex("ix_inventory_lots_product_warehouse", InventoryLot::productId, InventoryLot::warehouseId);
            model.hasForeignKey("fk_inventory_lots_warehouse", "warehouses", "warehouse_id", "NO_ACTION", "NO_ACTION", InventoryLot::warehouseId);
            model.hasForeignKey("fk_inventory_lots_product", "products", "product_id", "NO_ACTION", "NO_ACTION", InventoryLot::productId);
            model.hasCheck("ck_inventory_lots_on_hand", "on_hand >= 0");
            model.hasCheck("ck_inventory_lots_reserved", "reserved >= 0");
            model.hasCheck("ck_inventory_lots_quarantined", "quarantined >= 0");
        }
    }

    static final class CustomerOrderStorage extends FoundginePersistenceConfiguration<CustomerOrder> {
        @Override
        public void configure(FoundginePersistenceBuilder<CustomerOrder> model) {
            model.toTable("CustomerOrder");
            model.hasKey(CustomerOrder::id);
        }
    }

    static final class CustomerOrderLineStorage extends FoundginePersistenceConfiguration<CustomerOrderLine> {
        @Override
        public void configure(FoundginePersistenceBuilder<CustomerOrderLine> model) {
            model.toTable("CustomerOrderLine");
            model.hasKey(CustomerOrderLine::id);
        }
    }

    static final class InventoryMovementStorage extends FoundginePersistenceConfiguration<InventoryMovement> {
        @Override
        public void configure(FoundginePersistenceBuilder<InventoryMovement> model) {
            model.toTable("InventoryMovement");
            model.hasKey(InventoryMovement::id);
        }
    }

    static final class ProductionOutputStorage extends FoundginePersistenceConfiguration<ProductionOutput> {
        @Override
        public void configure(FoundginePersistenceBuilder<ProductionOutput> model) {
            model.toTable("ProductionOutput");
            model.hasKey(ProductionOutput::id);
        }
    }

    static final class ComplianceIncidentStorage extends FoundginePersistenceConfiguration<ComplianceIncident> {
        @Override
        public void configure(FoundginePersistenceBuilder<ComplianceIncident> model) {
            model.toTable("ComplianceIncident");
            model.hasKey(ComplianceIncident::id);
        }
    }

    // Transactional PostgreSQL rows. These physical row types intentionally do not reuse the
    // similarly named semantic CustomerOrder/PurchaseOrder/Supplier concepts.
    static final class CustomerRowStorage extends FoundginePersistenceConfiguration<AdvancedPersistenceRows.CustomerRow> {
        @Override public void configure(FoundginePersistenceBuilder<AdvancedPersistenceRows.CustomerRow> model) {
            model.toTable("customers");
            model.hasKey(AdvancedPersistenceRows.CustomerRow::customerId);
            model.property(AdvancedPersistenceRows.CustomerRow::customerId).hasColumnName("customer_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.CustomerRow::customerName).hasColumnName("customer_name").hasMaxLength(120).isRequired();
            model.property(AdvancedPersistenceRows.CustomerRow::tenantId).hasColumnName("tenant_id").hasMaxLength(80).isRequired();
        }
    }

    static final class OrderRowStorage extends FoundginePersistenceConfiguration<AdvancedPersistenceRows.OrderRow> {
        @Override public void configure(FoundginePersistenceBuilder<AdvancedPersistenceRows.OrderRow> model) {
            model.toTable("orders");
            model.hasKey(AdvancedPersistenceRows.OrderRow::orderId);
            model.property(AdvancedPersistenceRows.OrderRow::orderId).hasColumnName("order_id").hasColumnType("integer").isIdentityAlways().isRequired();
            model.property(AdvancedPersistenceRows.OrderRow::customerId).hasColumnName("customer_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.OrderRow::status).hasColumnName("status").hasMaxLength(30).isRequired();
            model.property(AdvancedPersistenceRows.OrderRow::totalAmount).hasColumnName("total_amount").hasPrecision(18, 2).isRequired();
            model.property(AdvancedPersistenceRows.OrderRow::placedOn).hasColumnName("placed_on").hasColumnType("date").isRequired();
            model.hasIndex("ix_orders_customer_status", AdvancedPersistenceRows.OrderRow::customerId, AdvancedPersistenceRows.OrderRow::status);
            model.hasForeignKey("fk_orders_customer", "customers", "customer_id", "NO_ACTION", "NO_ACTION", AdvancedPersistenceRows.OrderRow::customerId);
            model.hasCheck("ck_orders_status", "status IN ('Pending','Cancelled','Processing','Shipped','Delivered')");
        }
    }

    static final class OrderItemRowStorage extends FoundginePersistenceConfiguration<AdvancedPersistenceRows.OrderItemRow> {
        @Override public void configure(FoundginePersistenceBuilder<AdvancedPersistenceRows.OrderItemRow> model) {
            model.toTable("order_items");
            model.hasKey(AdvancedPersistenceRows.OrderItemRow::orderItemId);
            model.property(AdvancedPersistenceRows.OrderItemRow::orderItemId).hasColumnName("order_item_id").hasColumnType("integer").isIdentityAlways().isRequired();
            model.property(AdvancedPersistenceRows.OrderItemRow::orderId).hasColumnName("order_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.OrderItemRow::productId).hasColumnName("product_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.OrderItemRow::quantity).hasColumnName("quantity").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.OrderItemRow::unitPrice).hasColumnName("unit_price").hasPrecision(18, 2).isRequired();
            model.hasForeignKey("fk_order_items_order", "orders", "order_id", "CASCADE", "NO_ACTION", AdvancedPersistenceRows.OrderItemRow::orderId);
            model.hasForeignKey("fk_order_items_product", "products", "product_id", "NO_ACTION", "NO_ACTION", AdvancedPersistenceRows.OrderItemRow::productId);
            model.hasCheck("ck_order_items_quantity", "quantity > 0");
        }
    }

    static final class OrderAllocationRowStorage extends FoundginePersistenceConfiguration<AdvancedPersistenceRows.OrderAllocationRow> {
        @Override public void configure(FoundginePersistenceBuilder<AdvancedPersistenceRows.OrderAllocationRow> model) {
            model.toTable("order_allocations");
            model.hasKey(AdvancedPersistenceRows.OrderAllocationRow::orderItemId);
            model.property(AdvancedPersistenceRows.OrderAllocationRow::orderItemId).hasColumnName("order_item_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.OrderAllocationRow::lotId).hasColumnName("lot_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.OrderAllocationRow::quantity).hasColumnName("quantity").hasColumnType("integer").isRequired();
            model.hasForeignKey("fk_order_allocations_order_item", "order_items", "order_item_id", "CASCADE", "NO_ACTION", AdvancedPersistenceRows.OrderAllocationRow::orderItemId);
            model.hasForeignKey("fk_order_allocations_lot", "inventory_lots", "lot_id", "NO_ACTION", "NO_ACTION", AdvancedPersistenceRows.OrderAllocationRow::lotId);
            model.hasCheck("ck_order_allocations_quantity", "quantity > 0");
        }
    }

    static final class IdempotencyRowStorage extends FoundginePersistenceConfiguration<AdvancedPersistenceRows.IdempotencyRow> {
        @Override public void configure(FoundginePersistenceBuilder<AdvancedPersistenceRows.IdempotencyRow> model) {
            model.toTable("supply_chain_idempotency");
            model.hasKey(AdvancedPersistenceRows.IdempotencyRow::idempotencyKey);
            model.property(AdvancedPersistenceRows.IdempotencyRow::idempotencyKey).hasColumnName("idempotency_key").hasMaxLength(200).isRequired();
            model.property(AdvancedPersistenceRows.IdempotencyRow::actor).hasColumnName("actor").hasMaxLength(200).isRequired();
            model.property(AdvancedPersistenceRows.IdempotencyRow::customerId).hasColumnName("customer_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.IdempotencyRow::orderId).hasColumnName("order_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.IdempotencyRow::requestFingerprint).hasColumnName("request_fingerprint").hasColumnType("char(64)").isRequired();
            model.property(AdvancedPersistenceRows.IdempotencyRow::createdAt).hasColumnName("created_at").hasColumnType("timestamptz").hasDefault("CURRENT_TIMESTAMP").isRequired();
            model.hasForeignKey("fk_supply_chain_idempotency_customer", "customers", "customer_id", "NO_ACTION", "NO_ACTION", AdvancedPersistenceRows.IdempotencyRow::customerId);
            model.hasForeignKey("fk_supply_chain_idempotency_order", "orders", "order_id", "NO_ACTION", "NO_ACTION", AdvancedPersistenceRows.IdempotencyRow::orderId);
        }
    }

    static final class CancellationIdempotencyRowStorage extends FoundginePersistenceConfiguration<AdvancedPersistenceRows.CancellationIdempotencyRow> {
        @Override public void configure(FoundginePersistenceBuilder<AdvancedPersistenceRows.CancellationIdempotencyRow> model) {
            model.toTable("supply_chain_cancellation_idempotency");
            model.hasKey(AdvancedPersistenceRows.CancellationIdempotencyRow::idempotencyKey);
            model.property(AdvancedPersistenceRows.CancellationIdempotencyRow::idempotencyKey).hasColumnName("idempotency_key").hasMaxLength(200).isRequired();
            model.property(AdvancedPersistenceRows.CancellationIdempotencyRow::actor).hasColumnName("actor").hasMaxLength(200).isRequired();
            model.property(AdvancedPersistenceRows.CancellationIdempotencyRow::orderId).hasColumnName("order_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.CancellationIdempotencyRow::requestFingerprint).hasColumnName("request_fingerprint").hasColumnType("char(64)").isRequired();
            model.property(AdvancedPersistenceRows.CancellationIdempotencyRow::restoredQuantity).hasColumnName("restored_quantity").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.CancellationIdempotencyRow::cancelledOn).hasColumnName("cancelled_on").hasColumnType("date").isRequired();
            model.hasForeignKey("fk_supply_chain_cancellation_order", "orders", "order_id", "NO_ACTION", "NO_ACTION", AdvancedPersistenceRows.CancellationIdempotencyRow::orderId);
        }
    }


    // Supplier search tables used by TopSupplierOverdueOrdersService. These map the physical
    // PostgreSQL tables, not the separate semantic Supplier/PurchaseOrder records above.
    static final class SupplierRowStorage extends FoundginePersistenceConfiguration<AdvancedPersistenceRows.SupplierRow> {
        @Override public void configure(FoundginePersistenceBuilder<AdvancedPersistenceRows.SupplierRow> model) {
            model.toTable("suppliers");
            model.hasKey(AdvancedPersistenceRows.SupplierRow::supplierId);
            model.property(AdvancedPersistenceRows.SupplierRow::supplierId).hasColumnName("supplier_id").hasColumnType("integer").isIdentityAlways().isRequired();
            model.property(AdvancedPersistenceRows.SupplierRow::supplierName).hasColumnName("supplier_name").hasMaxLength(100).isRequired();
            model.property(AdvancedPersistenceRows.SupplierRow::state).hasColumnName("state").hasMaxLength(2);
            model.property(AdvancedPersistenceRows.SupplierRow::totalOrderValue).hasColumnName("total_order_value").hasPrecision(14, 2).hasDefault("0").isRequired();
            model.property(AdvancedPersistenceRows.SupplierRow::negotiatedCost).hasColumnName("negotiated_cost").hasPrecision(14, 2);
            model.hasUniqueIndex("suppliers_supplier_name_key", AdvancedPersistenceRows.SupplierRow::supplierName);
            model.hasExtension("pg_trgm");
            model.hasIndexUsing("ix_suppliers_name_trgm", "gin", "gin_trgm_ops", AdvancedPersistenceRows.SupplierRow::supplierName);
        }
    }

    static final class PurchaseOrderRowStorage extends FoundginePersistenceConfiguration<AdvancedPersistenceRows.PurchaseOrderRow> {
        @Override public void configure(FoundginePersistenceBuilder<AdvancedPersistenceRows.PurchaseOrderRow> model) {
            model.toTable("purchase_orders");
            model.hasKey(AdvancedPersistenceRows.PurchaseOrderRow::purchaseOrderId);
            model.property(AdvancedPersistenceRows.PurchaseOrderRow::purchaseOrderId).hasColumnName("purchase_order_id").hasColumnType("integer").isIdentityAlways().isRequired();
            model.property(AdvancedPersistenceRows.PurchaseOrderRow::supplierId).hasColumnName("supplier_id").hasColumnType("integer").isRequired();
            model.property(AdvancedPersistenceRows.PurchaseOrderRow::expectedDate).hasColumnName("expected_date").hasColumnType("date").isRequired();
            model.property(AdvancedPersistenceRows.PurchaseOrderRow::receivedDate).hasColumnName("received_date").hasColumnType("date");
            model.property(AdvancedPersistenceRows.PurchaseOrderRow::status).hasColumnName("status").hasMaxLength(20).hasDefault("'Open'").isRequired();
            model.hasIndex("ix_purchase_orders_supplier", AdvancedPersistenceRows.PurchaseOrderRow::supplierId);
            model.hasForeignKey("fk_purchase_orders_supplier", "suppliers", "supplier_id", "NO_ACTION", "NO_ACTION", AdvancedPersistenceRows.PurchaseOrderRow::supplierId);
            model.hasCheck("ck_purchase_orders_status", "status IN ('Open','Received','Cancelled')");
        }
    }

}