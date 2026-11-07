using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Domain;

// Semantic configuration: entity name and relationships only. Tables and columns
// are in PersistenceConfigurations.cs. ToEntity keeps the semantic entity names the
// attribute-based declarations used ("CustomerERP", ...); drop the calls to name
// entities after the models instead.

internal sealed class CustomerModelConfiguration : FoundgineModelConfiguration<Customer>
{
    public override void Configure(FoundgineModelBuilder<Customer> model)
    {
        model.ToEntity("CustomerERP");
        model.HasRelationship(x => x.Orders).To<SalesOrder>();
    }
}

internal sealed class SalesOrderModelConfiguration : FoundgineModelConfiguration<SalesOrder>
{
    public override void Configure(FoundgineModelBuilder<SalesOrder> model)
    {
        model.ToEntity("SalesOrderERP");
        model.HasRelationship(x => x.Lines).To<SalesOrderLine>();
        model.HasRelationship(x => x.Shipments).To<Shipment>();
    }
}

internal sealed class SalesOrderLineModelConfiguration : FoundgineModelConfiguration<SalesOrderLine>
{
    public override void Configure(FoundgineModelBuilder<SalesOrderLine> model)
    {
        model.ToEntity("SalesOrderLineERP");
        model.HasRelationship(x => x.Product).To<CatalogProduct>();
    }
}

internal sealed class CatalogProductModelConfiguration : FoundgineModelConfiguration<CatalogProduct>
{
    public override void Configure(FoundgineModelBuilder<CatalogProduct> model)
    {
        model.ToEntity("CatalogProductERP");
        model.HasRelationship(x => x.Supplier).To<Supplier>();
        model.HasRelationship(x => x.Category).To<Category>();
        model.HasRelationship(x => x.InventoryPositions).To<InventoryPosition>();
    }
}

internal sealed class SupplierModelConfiguration : FoundgineModelConfiguration<Supplier>
{
    public override void Configure(FoundgineModelBuilder<Supplier> model) =>
        model.ToEntity("SupplierERP");
}

internal sealed class CategoryModelConfiguration : FoundgineModelConfiguration<Category>
{
    public override void Configure(FoundgineModelBuilder<Category> model) =>
        model.ToEntity("CategoryERP");
}

internal sealed class InventoryPositionModelConfiguration : FoundgineModelConfiguration<InventoryPosition>
{
    public override void Configure(FoundgineModelBuilder<InventoryPosition> model)
    {
        model.ToEntity("InventoryPositionERP");
        model.HasRelationship(x => x.Warehouse).To<Warehouse>();
    }
}

internal sealed class WarehouseModelConfiguration : FoundgineModelConfiguration<Warehouse>
{
    public override void Configure(FoundgineModelBuilder<Warehouse> model) =>
        model.ToEntity("WarehouseERP");
}

internal sealed class ShipmentModelConfiguration : FoundgineModelConfiguration<Shipment>
{
    public override void Configure(FoundgineModelBuilder<Shipment> model)
    {
        model.ToEntity("ShipmentERP");
        model.HasRelationship(x => x.Order).To<SalesOrder>();
        model.HasRelationship(x => x.Carrier).To<Carrier>();
        model.HasRelationship(x => x.Warehouse).To<Warehouse>();
    }
}

internal sealed class CarrierModelConfiguration : FoundgineModelConfiguration<Carrier>
{
    public override void Configure(FoundgineModelBuilder<Carrier> model) =>
        model.ToEntity("CarrierERP");
}
