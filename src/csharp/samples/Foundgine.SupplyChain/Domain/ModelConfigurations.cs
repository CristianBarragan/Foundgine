using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Domain;

// Semantic configuration: relationships only. Entity names default to the type names
// (Customer, SalesOrder, ...). Tables and columns are in PersistenceConfigurations.cs.

/// <summary>An entity with no relationships of its own.</summary>
internal sealed class Plain<T> : FoundgineModelConfiguration<T>
{
    public override void Configure(FoundgineModelBuilder<T> model)
    {
    }
}

internal sealed class CustomerModel : FoundgineModelConfiguration<Customer>
{
    public override void Configure(FoundgineModelBuilder<Customer> model) =>
        model.HasRelationship(x => x.Orders).To<SalesOrder>();
}

internal sealed class SalesOrderModel : FoundgineModelConfiguration<SalesOrder>
{
    public override void Configure(FoundgineModelBuilder<SalesOrder> model)
    {
        model.HasRelationship(x => x.Lines).To<SalesOrderLine>();
        model.HasRelationship(x => x.Shipments).To<Shipment>();
    }
}

internal sealed class SalesOrderLineModel : FoundgineModelConfiguration<SalesOrderLine>
{
    public override void Configure(FoundgineModelBuilder<SalesOrderLine> model) =>
        model.HasRelationship(x => x.Product).To<CatalogProduct>();
}

internal sealed class CatalogProductModel : FoundgineModelConfiguration<CatalogProduct>
{
    public override void Configure(FoundgineModelBuilder<CatalogProduct> model)
    {
        model.HasRelationship(x => x.Supplier).To<Supplier>();
        model.HasRelationship(x => x.Category).To<Category>();
        model.HasRelationship(x => x.InventoryPositions).To<InventoryPosition>();
    }
}

internal sealed class InventoryPositionModel : FoundgineModelConfiguration<InventoryPosition>
{
    public override void Configure(FoundgineModelBuilder<InventoryPosition> model) =>
        model.HasRelationship(x => x.Warehouse).To<Warehouse>();
}

internal sealed class ShipmentModel : FoundgineModelConfiguration<Shipment>
{
    public override void Configure(FoundgineModelBuilder<Shipment> model)
    {
        model.HasRelationship(x => x.Order).To<SalesOrder>();
        model.HasRelationship(x => x.Carrier).To<Carrier>();
        model.HasRelationship(x => x.Warehouse).To<Warehouse>();
    }
}
