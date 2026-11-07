using Foundgine.Core.Semantic.Configuration;

namespace Foundgine.SupplyChain.Domain;

/// <summary>
/// The supply-chain Foundgine model: the single registration point for every
/// semantic, persistence, mapping and connection configuration. Order does not matter.
/// <code>MetadataRegistry registry = new SupplyChainModel().Build();</code>
/// To add to an existing registry use
/// <c>new SupplyChainModel().BuildInto(existingRegistry)</c>.
/// </summary>
public sealed class SupplyChainModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        // Semantic models
        model.Configure<CustomerModelConfiguration>();
        model.Configure<SalesOrderModelConfiguration>();
        model.Configure<SalesOrderLineModelConfiguration>();
        model.Configure<CatalogProductModelConfiguration>();
        model.Configure<SupplierModelConfiguration>();
        model.Configure<CategoryModelConfiguration>();
        model.Configure<InventoryPositionModelConfiguration>();
        model.Configure<WarehouseModelConfiguration>();
        model.Configure<ShipmentModelConfiguration>();
        model.Configure<CarrierModelConfiguration>();

        // Persistence
        model.Configure<CustomerERPConfiguration>();
        model.Configure<SalesOrderERPConfiguration>();
        model.Configure<SalesOrderLineERPConfiguration>();
        model.Configure<CatalogProductERPConfiguration>();
        model.Configure<SupplierERPConfiguration>();
        model.Configure<CategoryERPConfiguration>();
        model.Configure<InventoryPositionERPConfiguration>();
        model.Configure<WarehouseERPConfiguration>();
        model.Configure<ShipmentERPConfiguration>();
        model.Configure<CarrierERPConfiguration>();

        // Model -> storage maps
        model.Configure<CustomerEntityMap>();
        model.Configure<SalesOrderEntityMap>();
        model.Configure<SalesOrderLineEntityMap>();
        model.Configure<CatalogProductEntityMap>();
        model.Configure<SupplierEntityMap>();
        model.Configure<CategoryEntityMap>();
        model.Configure<InventoryPositionEntityMap>();
        model.Configure<WarehouseEntityMap>();
        model.Configure<ShipmentEntityMap>();
        model.Configure<CarrierEntityMap>();

        // Relationship -> foreign key
        model.Configure<SupplyChainConnections>();
    }
}
