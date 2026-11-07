using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Models;
using Foundgine.SupplyChain.Domain.Storage;

namespace Foundgine.SupplyChain.Domain;

// Schema-bound mappings. Keeping them out of both the application models and the
// ERP storage types prevents either from depending on the other CLR type.

// ---- model member -> storage member ---------------------------------------------

internal sealed class CustomerEntityMap : FoundgineEntityMap<Customer, CustomerERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Customer, CustomerERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.FirstName).To(e => e.FirstName)
            .Property(m => m.LastName).To(e => e.LastName)
            .Property(m => m.Email).To(e => e.Email);
}

internal sealed class SalesOrderEntityMap : FoundgineEntityMap<SalesOrder, SalesOrderERP>
{
    public override void Configure(FoundgineEntityMapBuilder<SalesOrder, SalesOrderERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.CustomerId).To(e => e.CustomerId)
            .Property(m => m.Status).To(e => e.Status)
            .Property(m => m.TotalAmount).To(e => e.TotalAmount);
}

internal sealed class SalesOrderLineEntityMap : FoundgineEntityMap<SalesOrderLine, SalesOrderLineERP>
{
    public override void Configure(FoundgineEntityMapBuilder<SalesOrderLine, SalesOrderLineERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.OrderId).To(e => e.OrderId)
            .Property(m => m.ProductId).To(e => e.ProductId)
            .Property(m => m.Quantity).To(e => e.Quantity)
            .Property(m => m.UnitPrice).To(e => e.UnitPrice);
}

internal sealed class CatalogProductEntityMap : FoundgineEntityMap<CatalogProduct, CatalogProductERP>
{
    public override void Configure(FoundgineEntityMapBuilder<CatalogProduct, CatalogProductERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name)
            .Property(m => m.Sku).To(e => e.Sku)
            .Property(m => m.UnitPrice).To(e => e.UnitPrice);
}

internal sealed class SupplierEntityMap : FoundgineEntityMap<Supplier, SupplierERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Supplier, SupplierERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name)
            .Property(m => m.Email).To(e => e.Email);
}

internal sealed class CategoryEntityMap : FoundgineEntityMap<Category, CategoryERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Category, CategoryERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name);
}

internal sealed class InventoryPositionEntityMap : FoundgineEntityMap<InventoryPosition, InventoryPositionERP>
{
    public override void Configure(FoundgineEntityMapBuilder<InventoryPosition, InventoryPositionERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.WarehouseId).To(e => e.WarehouseId)
            .Property(m => m.ProductId).To(e => e.ProductId)
            .Property(m => m.QuantityOnHand).To(e => e.QuantityOnHand)
            .Property(m => m.ReorderLevel).To(e => e.ReorderLevel);
}

internal sealed class WarehouseEntityMap : FoundgineEntityMap<Warehouse, WarehouseERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Warehouse, WarehouseERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name)
            .Property(m => m.Location).To(e => e.Location);
}

internal sealed class ShipmentEntityMap : FoundgineEntityMap<Shipment, ShipmentERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Shipment, ShipmentERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.OrderId).To(e => e.OrderId)
            .Property(m => m.CarrierId).To(e => e.CarrierId)
            .Property(m => m.WarehouseId).To(e => e.WarehouseId)
            .Property(m => m.TrackingNumber).To(e => e.TrackingNumber)
            .Property(m => m.Status).To(e => e.Status);
}

internal sealed class CarrierEntityMap : FoundgineEntityMap<Carrier, CarrierERP>
{
    public override void Configure(FoundgineEntityMapBuilder<Carrier, CarrierERP> map) =>
        map
            .Property(m => m.Id).To(e => e.Id)
            .Property(m => m.Name).To(e => e.Name);
}

// ---- relationship -> physical foreign key -----------------------------------------
// One connection configures both directions of a pair, so SalesOrder.Shipments and
// Shipment.Order share a single connection. HasForeignKey selects the key on the
// target storage type (one-to-many); HasForeignKeyOnSource selects it on the source
// (many-to-one).

internal sealed class SupplyChainConnections : FoundgineConnectionConfiguration
{
    public override void Configure(FoundgineConnectionBuilder connections)
    {
        // Customer 1 - * SalesOrder
        connections.HasRelationship<Customer, SalesOrder>()
            .Using<CustomerERP, SalesOrderERP>()
            .HasForeignKey(o => o.CustomerId)
            .References(c => c.Id);

        // SalesOrder 1 - * SalesOrderLine
        connections.HasRelationship<SalesOrder, SalesOrderLine>()
            .Using<SalesOrderERP, SalesOrderLineERP>()
            .HasForeignKey(l => l.OrderId)
            .References(o => o.Id);

        // SalesOrder 1 - * Shipment (also defines Shipment.Order)
        connections.HasRelationship<SalesOrder, Shipment>()
            .Using<SalesOrderERP, ShipmentERP>()
            .HasForeignKey(s => s.OrderId)
            .References(o => o.Id);

        // SalesOrderLine * - 1 CatalogProduct
        connections.HasRelationship<SalesOrderLine, CatalogProduct>()
            .Using<SalesOrderLineERP, CatalogProductERP>()
            .HasForeignKeyOnSource(l => l.ProductId)
            .References(p => p.Id);

        // CatalogProduct * - 1 Supplier
        connections.HasRelationship<CatalogProduct, Supplier>()
            .Using<CatalogProductERP, SupplierERP>()
            .HasForeignKeyOnSource(p => p.SupplierId)
            .References(s => s.Id);

        // CatalogProduct * - 1 Category
        connections.HasRelationship<CatalogProduct, Category>()
            .Using<CatalogProductERP, CategoryERP>()
            .HasForeignKeyOnSource(p => p.CategoryId)
            .References(c => c.Id);

        // CatalogProduct 1 - * InventoryPosition
        connections.HasRelationship<CatalogProduct, InventoryPosition>()
            .Using<CatalogProductERP, InventoryPositionERP>()
            .HasForeignKey(i => i.ProductId)
            .References(p => p.Id);

        // InventoryPosition * - 1 Warehouse
        connections.HasRelationship<InventoryPosition, Warehouse>()
            .Using<InventoryPositionERP, WarehouseERP>()
            .HasForeignKeyOnSource(i => i.WarehouseId)
            .References(w => w.Id);

        // Shipment * - 1 Carrier
        connections.HasRelationship<Shipment, Carrier>()
            .Using<ShipmentERP, CarrierERP>()
            .HasForeignKeyOnSource(s => s.CarrierId)
            .References(c => c.Id);

        // Shipment * - 1 Warehouse
        connections.HasRelationship<Shipment, Warehouse>()
            .Using<ShipmentERP, WarehouseERP>()
            .HasForeignKeyOnSource(s => s.WarehouseId)
            .References(w => w.Id);
    }
}
