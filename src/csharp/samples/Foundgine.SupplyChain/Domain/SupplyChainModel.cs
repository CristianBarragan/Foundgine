using Foundgine.Core.Semantic.Configuration;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Domain;

/// <summary>
/// The supply-chain Foundgine model: the single registration point for the semantic model, the persistence
/// model and the relationship connections. Order does not matter.
/// <code>MetadataRegistry registry = new SupplyChainModel().Build();</code>
/// Each domain class is both semantic model and EF entity, so <see cref="IdentityEntityMap{T}"/> replaces the
/// model-to-storage mapping layer.
/// </summary>
public sealed class SupplyChainModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        // Semantic: entity names (default = type name) and relationships
        model.Configure<CustomerModel>();
        model.Configure<SalesOrderModel>();
        model.Configure<SalesOrderLineModel>();
        model.Configure<CatalogProductModel>();
        model.Configure<InventoryPositionModel>();
        model.Configure<ShipmentModel>();
        model.Configure<Plain<Supplier>>();
        model.Configure<Plain<Category>>();
        model.Configure<Plain<Warehouse>>();
        model.Configure<Plain<Carrier>>();

        // Persistence: table, key, columns
        model.Configure<CustomerTable>();
        model.Configure<SalesOrderTable>();
        model.Configure<SalesOrderLineTable>();
        model.Configure<CatalogProductTable>();
        model.Configure<SupplierTable>();
        model.Configure<CategoryTable>();
        model.Configure<InventoryPositionTable>();
        model.Configure<WarehouseTable>();
        model.Configure<ShipmentTable>();
        model.Configure<CarrierTable>();

        // Model == entity: identity maps (no mapping layer)
        model.Configure<IdentityEntityMap<Customer>>();
        model.Configure<IdentityEntityMap<SalesOrder>>();
        model.Configure<IdentityEntityMap<SalesOrderLine>>();
        model.Configure<IdentityEntityMap<CatalogProduct>>();
        model.Configure<IdentityEntityMap<Supplier>>();
        model.Configure<IdentityEntityMap<Category>>();
        model.Configure<IdentityEntityMap<InventoryPosition>>();
        model.Configure<IdentityEntityMap<Warehouse>>();
        model.Configure<IdentityEntityMap<Shipment>>();
        model.Configure<IdentityEntityMap<Carrier>>();

        // Relationship -> foreign key
        model.Configure<SupplyChainConnections>();
    }
}
