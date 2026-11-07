using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Advanced.Semantics.Configuration;

namespace Foundgine.SupplyChain.Advanced.Semantics;

/// <summary>
/// Single registration point for the Supply Chain model: semantic configuration,
/// persistence configuration, entity maps and connections. <c>Build()</c> validates the
/// whole set and produces the metadata registry the rest of the sample consumes.
/// </summary>
public sealed class SupplyChainModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        // Semantic model
        model.Configure<CompanyModelConfiguration>();
        model.Configure<BusinessUnitModelConfiguration>();
        model.Configure<WarehouseModelConfiguration>();
        model.Configure<SupplierModelConfiguration>();
        model.Configure<SupplierSiteModelConfiguration>();
        model.Configure<SupplierCertificationModelConfiguration>();
        model.Configure<ProductModelConfiguration>();
        model.Configure<ProductComponentModelConfiguration>();
        model.Configure<PurchaseOrderModelConfiguration>();
        model.Configure<PurchaseOrderLineModelConfiguration>();
        model.Configure<ShipmentModelConfiguration>();
        model.Configure<InventoryLotModelConfiguration>();
        model.Configure<CustomerOrderModelConfiguration>();
        model.Configure<CustomerOrderLineModelConfiguration>();
        model.Configure<InventoryMovementModelConfiguration>();
        model.Configure<ProductionOutputModelConfiguration>();
        model.Configure<ComplianceIncidentModelConfiguration>();

        // Persistence model
        model.Configure<CompanyPersistenceConfiguration>();
        model.Configure<BusinessUnitPersistenceConfiguration>();
        model.Configure<WarehousePersistenceConfiguration>();
        model.Configure<SupplierPersistenceConfiguration>();
        model.Configure<SupplierSitePersistenceConfiguration>();
        model.Configure<SupplierCertificationPersistenceConfiguration>();
        model.Configure<ProductPersistenceConfiguration>();
        model.Configure<ProductComponentPersistenceConfiguration>();
        model.Configure<PurchaseOrderPersistenceConfiguration>();
        model.Configure<PurchaseOrderLinePersistenceConfiguration>();
        model.Configure<ShipmentPersistenceConfiguration>();
        model.Configure<InventoryLotPersistenceConfiguration>();
        model.Configure<CustomerOrderPersistenceConfiguration>();
        model.Configure<CustomerOrderLinePersistenceConfiguration>();
        model.Configure<InventoryMovementPersistenceConfiguration>();
        model.Configure<ProductionOutputPersistenceConfiguration>();
        model.Configure<ComplianceIncidentPersistenceConfiguration>();

        // Model-to-persistence mapping
        model.Configure<CompanyEntityMap>();
        model.Configure<BusinessUnitEntityMap>();
        model.Configure<WarehouseEntityMap>();
        model.Configure<SupplierEntityMap>();
        model.Configure<SupplierSiteEntityMap>();
        model.Configure<SupplierCertificationEntityMap>();
        model.Configure<ProductEntityMap>();
        model.Configure<ProductComponentEntityMap>();
        model.Configure<PurchaseOrderEntityMap>();
        model.Configure<PurchaseOrderLineEntityMap>();
        model.Configure<ShipmentEntityMap>();
        model.Configure<InventoryLotEntityMap>();
        model.Configure<CustomerOrderEntityMap>();
        model.Configure<CustomerOrderLineEntityMap>();
        model.Configure<InventoryMovementEntityMap>();
        model.Configure<ProductionOutputEntityMap>();
        model.Configure<ComplianceIncidentEntityMap>();

        // Relationships to physical keys
        model.Configure<SupplyChainConnections>();

        // Model-to-entity connections with field maps and authorization
        model.Configure<SupplyChainModelConnections>();
    }
}
