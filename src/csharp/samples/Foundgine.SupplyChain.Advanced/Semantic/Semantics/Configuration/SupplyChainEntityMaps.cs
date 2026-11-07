using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Advanced.Domain;

namespace Foundgine.SupplyChain.Advanced.Semantics.Configuration;

// Model member to storage member. The domain types double as the storage shape in
// this sample, so each type maps onto itself; with separate persistence classes only
// the second type argument and the To(...) selectors change.

public sealed class CompanyEntityMap : FoundgineEntityMap<Company, Company>
{
    public override void Configure(FoundgineEntityMapBuilder<Company, Company> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.Name).To(x => x.Name);
        map.Property(x => x.TenantId).To(x => x.TenantId);
    }
}

public sealed class BusinessUnitEntityMap : FoundgineEntityMap<BusinessUnit, BusinessUnit>
{
    public override void Configure(FoundgineEntityMapBuilder<BusinessUnit, BusinessUnit> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.CompanyId).To(x => x.CompanyId);
        map.Property(x => x.Name).To(x => x.Name);
    }
}

public sealed class WarehouseEntityMap : FoundgineEntityMap<Warehouse, Warehouse>
{
    public override void Configure(FoundgineEntityMapBuilder<Warehouse, Warehouse> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.BusinessUnitId).To(x => x.BusinessUnitId);
        map.Property(x => x.Name).To(x => x.Name);
        map.Property(x => x.TenantId).To(x => x.TenantId);
    }
}

public sealed class SupplierEntityMap : FoundgineEntityMap<Supplier, Supplier>
{
    public override void Configure(FoundgineEntityMapBuilder<Supplier, Supplier> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.Name).To(x => x.Name);
        map.Property(x => x.Country).To(x => x.Country);
        map.Property(x => x.RiskScore).To(x => x.RiskScore);
        map.Property(x => x.TenantId).To(x => x.TenantId);
    }
}

public sealed class SupplierSiteEntityMap : FoundgineEntityMap<SupplierSite, SupplierSite>
{
    public override void Configure(FoundgineEntityMapBuilder<SupplierSite, SupplierSite> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.SupplierId).To(x => x.SupplierId);
        map.Property(x => x.Country).To(x => x.Country);
        map.Property(x => x.Name).To(x => x.Name);
    }
}

public sealed class SupplierCertificationEntityMap : FoundgineEntityMap<SupplierCertification, SupplierCertification>
{
    public override void Configure(FoundgineEntityMapBuilder<SupplierCertification, SupplierCertification> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.SupplierId).To(x => x.SupplierId);
        map.Property(x => x.Type).To(x => x.Type);
        map.Property(x => x.ValidFrom).To(x => x.ValidFrom);
        map.Property(x => x.ValidTo).To(x => x.ValidTo);
    }
}

public sealed class ProductEntityMap : FoundgineEntityMap<Product, Product>
{
    public override void Configure(FoundgineEntityMapBuilder<Product, Product> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.Sku).To(x => x.Sku);
        map.Property(x => x.Name).To(x => x.Name);
        map.Property(x => x.Category).To(x => x.Category);
        map.Property(x => x.SafetyStock).To(x => x.SafetyStock);
    }
}

public sealed class ProductComponentEntityMap : FoundgineEntityMap<ProductComponent, ProductComponent>
{
    public override void Configure(FoundgineEntityMapBuilder<ProductComponent, ProductComponent> map)
    {
        map.Property(x => x.ParentProductId).To(x => x.ParentProductId);
        map.Property(x => x.ComponentProductId).To(x => x.ComponentProductId);
        map.Property(x => x.QuantityPerParent).To(x => x.QuantityPerParent);
        map.Property(x => x.EffectiveFrom).To(x => x.EffectiveFrom);
        map.Property(x => x.EffectiveTo).To(x => x.EffectiveTo);
        map.Property(x => x.Revision).To(x => x.Revision);
        map.Property(x => x.IsPhantom).To(x => x.IsPhantom);
        map.Property(x => x.YieldLossPercent).To(x => x.YieldLossPercent);
        map.Property(x => x.ScrapFactor).To(x => x.ScrapFactor);
    }
}

public sealed class PurchaseOrderEntityMap : FoundgineEntityMap<PurchaseOrder, PurchaseOrder>
{
    public override void Configure(FoundgineEntityMapBuilder<PurchaseOrder, PurchaseOrder> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.SupplierId).To(x => x.SupplierId);
        map.Property(x => x.WarehouseId).To(x => x.WarehouseId);
        map.Property(x => x.Status).To(x => x.Status);
        map.Property(x => x.OrderedOn).To(x => x.OrderedOn);
        map.Property(x => x.ExpectedArrival).To(x => x.ExpectedArrival);
    }
}

public sealed class PurchaseOrderLineEntityMap : FoundgineEntityMap<PurchaseOrderLine, PurchaseOrderLine>
{
    public override void Configure(FoundgineEntityMapBuilder<PurchaseOrderLine, PurchaseOrderLine> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.PurchaseOrderId).To(x => x.PurchaseOrderId);
        map.Property(x => x.ProductId).To(x => x.ProductId);
        map.Property(x => x.Quantity).To(x => x.Quantity);
        map.Property(x => x.UnitPrice).To(x => x.UnitPrice);
    }
}

public sealed class ShipmentEntityMap : FoundgineEntityMap<Shipment, Shipment>
{
    public override void Configure(FoundgineEntityMapBuilder<Shipment, Shipment> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.PurchaseOrderId).To(x => x.PurchaseOrderId);
        map.Property(x => x.ExpectedArrival).To(x => x.ExpectedArrival);
        map.Property(x => x.ActualArrival).To(x => x.ActualArrival);
        map.Property(x => x.Status).To(x => x.Status);
        map.Property(x => x.Quantity).To(x => x.Quantity);
    }
}

public sealed class InventoryLotEntityMap : FoundgineEntityMap<InventoryLot, InventoryLot>
{
    public override void Configure(FoundgineEntityMapBuilder<InventoryLot, InventoryLot> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.WarehouseId).To(x => x.WarehouseId);
        map.Property(x => x.ProductId).To(x => x.ProductId);
        map.Property(x => x.OnHand).To(x => x.OnHand);
        map.Property(x => x.Reserved).To(x => x.Reserved);
        map.Property(x => x.Quarantined).To(x => x.Quarantined);
        map.Property(x => x.ReceivedOn).To(x => x.ReceivedOn);
    }
}

public sealed class CustomerOrderEntityMap : FoundgineEntityMap<CustomerOrder, CustomerOrder>
{
    public override void Configure(FoundgineEntityMapBuilder<CustomerOrder, CustomerOrder> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.BusinessUnitId).To(x => x.BusinessUnitId);
        map.Property(x => x.PlacedOn).To(x => x.PlacedOn);
        map.Property(x => x.Status).To(x => x.Status);
    }
}

public sealed class CustomerOrderLineEntityMap : FoundgineEntityMap<CustomerOrderLine, CustomerOrderLine>
{
    public override void Configure(FoundgineEntityMapBuilder<CustomerOrderLine, CustomerOrderLine> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.CustomerOrderId).To(x => x.CustomerOrderId);
        map.Property(x => x.ProductId).To(x => x.ProductId);
        map.Property(x => x.Quantity).To(x => x.Quantity);
    }
}

public sealed class InventoryMovementEntityMap : FoundgineEntityMap<InventoryMovement, InventoryMovement>
{
    public override void Configure(FoundgineEntityMapBuilder<InventoryMovement, InventoryMovement> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.LotId).To(x => x.LotId);
        map.Property(x => x.Quantity).To(x => x.Quantity);
        map.Property(x => x.Reason).To(x => x.Reason);
        map.Property(x => x.OccurredAt).To(x => x.OccurredAt);
    }
}

public sealed class ProductionOutputEntityMap : FoundgineEntityMap<ProductionOutput, ProductionOutput>
{
    public override void Configure(FoundgineEntityMapBuilder<ProductionOutput, ProductionOutput> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.ProductionOrderId).To(x => x.ProductionOrderId);
        map.Property(x => x.ProductId).To(x => x.ProductId);
        map.Property(x => x.Quantity).To(x => x.Quantity);
    }
}

public sealed class ComplianceIncidentEntityMap : FoundgineEntityMap<ComplianceIncident, ComplianceIncident>
{
    public override void Configure(FoundgineEntityMapBuilder<ComplianceIncident, ComplianceIncident> map)
    {
        map.Property(x => x.Id).To(x => x.Id);
        map.Property(x => x.SupplierId).To(x => x.SupplierId);
        map.Property(x => x.Severity).To(x => x.Severity);
        map.Property(x => x.OccurredOn).To(x => x.OccurredOn);
        map.Property(x => x.Description).To(x => x.Description);
    }
}
