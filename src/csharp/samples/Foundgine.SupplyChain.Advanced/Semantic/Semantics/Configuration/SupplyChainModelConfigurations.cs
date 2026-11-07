using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Advanced.Domain;

namespace Foundgine.SupplyChain.Advanced.Semantics.Configuration;

// Semantic meaning only: entity names, aliases, dimensions, events and logical
// relationships. Nothing here mentions tables, columns or providers.

public sealed class CompanyModelConfiguration : FoundgineModelConfiguration<Company>
{
    public override void Configure(FoundgineModelBuilder<Company> model)
    {
        model.ToEntity("Company");
        model.HasDimension(x => x.TenantId).As("tenant");
    }
}

public sealed class BusinessUnitModelConfiguration : FoundgineModelConfiguration<BusinessUnit>
{
    public override void Configure(FoundgineModelBuilder<BusinessUnit> model)
    {
        model.ToEntity("BusinessUnit");
        model.HasDimension(x => x.CompanyId).As("company");
    }
}

public sealed class WarehouseModelConfiguration : FoundgineModelConfiguration<Warehouse>
{
    public override void Configure(FoundgineModelBuilder<Warehouse> model)
    {
        model.ToEntity("Warehouse");
        model.HasDimension(x => x.BusinessUnitId).As("businessUnit");
        model.HasDimension(x => x.TenantId).As("tenant");
        model.HasRelationship(x => x.BusinessUnit).To<BusinessUnit>().Named("businessUnit");
        model.HasRelationship(x => x.Inventory).To<InventoryLot>().Named("inventory");
    }
}

public sealed class SupplierModelConfiguration : FoundgineModelConfiguration<Supplier>
{
    public override void Configure(FoundgineModelBuilder<Supplier> model)
    {
        model.ToEntity("Supplier");
        model.HasAlias("Vendor", 95);
        model.HasAlias("Seller", 90);
        model.HasDimension(x => x.Country).As("country");
        model.HasDimension(x => x.TenantId).As("tenant");
        model.Property(x => x.Country).HasAlias("State", 85);
        model.HasRelationship(x => x.Certifications).To<SupplierCertification>().Named("certifications");
        model.HasRelationship(x => x.Incidents).To<ComplianceIncident>().Named("incidents");
        model.HasRelationship(x => x.PurchaseOrders).To<PurchaseOrder>().Named("purchaseOrders");
    }
}

public sealed class SupplierSiteModelConfiguration : FoundgineModelConfiguration<SupplierSite>
{
    public override void Configure(FoundgineModelBuilder<SupplierSite> model)
    {
        model.ToEntity("SupplierSite");
        model.HasDimension(x => x.Country).As("country");
    }
}

public sealed class SupplierCertificationModelConfiguration : FoundgineModelConfiguration<SupplierCertification>
{
    public override void Configure(FoundgineModelBuilder<SupplierCertification> model)
    {
        model.ToEntity("SupplierCertification");
    }
}

public sealed class ProductModelConfiguration : FoundgineModelConfiguration<Product>
{
    public override void Configure(FoundgineModelBuilder<Product> model)
    {
        model.ToEntity("Product");
        model.HasDimension(x => x.Category).As("category");
        model.HasRelationship(x => x.Components).To<ProductComponent>().Named("components");
        model.HasRelationship(x => x.PurchaseOrderLines).To<PurchaseOrderLine>().Named("purchaseOrderLines");
        model.HasRelationship(x => x.UsedInComponents).To<ProductComponent>().Named("usedInComponents");
    }
}

public sealed class ProductComponentModelConfiguration : FoundgineModelConfiguration<ProductComponent>
{
    public override void Configure(FoundgineModelBuilder<ProductComponent> model)
    {
        model.ToEntity("ProductComponent");
        model.HasRelationship(x => x.ComponentProduct).To<Product>().Named("componentProduct").WithMany(x => x.UsedInComponents);
    }
}

public sealed class PurchaseOrderModelConfiguration : FoundgineModelConfiguration<PurchaseOrder>
{
    public override void Configure(FoundgineModelBuilder<PurchaseOrder> model)
    {
        model.ToEntity("PurchaseOrder");
        model.HasAlias("PO", 100);
        model.HasAlias("POs", 95);
        model.HasAlias("Buy", 90);
        model.HasAlias("Buys", 85);
        model.Property(x => x.ExpectedArrival).HasAlias("DueDate", 90);
        model.HasRelationship(x => x.Lines).To<PurchaseOrderLine>().Named("lines");
        model.HasRelationship(x => x.Shipments).To<Shipment>().Named("shipments");
        model.HasRelationship(x => x.Supplier).To<Supplier>().Named("supplier")
            .HasAlias("vendor", 85);
    }
}

public sealed class PurchaseOrderLineModelConfiguration : FoundgineModelConfiguration<PurchaseOrderLine>
{
    public override void Configure(FoundgineModelBuilder<PurchaseOrderLine> model)
    {
        model.ToEntity("PurchaseOrderLine");
        model.HasRelationship(x => x.PurchaseOrder).To<PurchaseOrder>().Named("purchaseOrder");
    }
}

public sealed class ShipmentModelConfiguration : FoundgineModelConfiguration<Shipment>
{
    public override void Configure(FoundgineModelBuilder<Shipment> model)
    {
        model.ToEntity("Shipment");
    }
}

public sealed class InventoryLotModelConfiguration : FoundgineModelConfiguration<InventoryLot>
{
    public override void Configure(FoundgineModelBuilder<InventoryLot> model)
    {
        model.ToEntity("InventoryLot");
        model.HasRelationship(x => x.Warehouse).To<Warehouse>().Named("warehouse");
    }
}

public sealed class CustomerOrderModelConfiguration : FoundgineModelConfiguration<CustomerOrder>
{
    public override void Configure(FoundgineModelBuilder<CustomerOrder> model)
    {
        model.ToEntity("CustomerOrder");
        model.HasRelationship(x => x.Lines).To<CustomerOrderLine>().Named("lines");
    }
}

public sealed class CustomerOrderLineModelConfiguration : FoundgineModelConfiguration<CustomerOrderLine>
{
    public override void Configure(FoundgineModelBuilder<CustomerOrderLine> model)
    {
        model.ToEntity("CustomerOrderLine");
    }
}

public sealed class InventoryMovementModelConfiguration : FoundgineModelConfiguration<InventoryMovement>
{
    public override void Configure(FoundgineModelBuilder<InventoryMovement> model)
    {
        model.ToEntity("InventoryMovement");
        model.IsEvent(x => x.OccurredAt);
    }
}

public sealed class ProductionOutputModelConfiguration : FoundgineModelConfiguration<ProductionOutput>
{
    public override void Configure(FoundgineModelBuilder<ProductionOutput> model)
    {
        model.ToEntity("ProductionOutput");
        model.IsEvent();
    }
}

public sealed class ComplianceIncidentModelConfiguration : FoundgineModelConfiguration<ComplianceIncident>
{
    public override void Configure(FoundgineModelBuilder<ComplianceIncident> model)
    {
        model.ToEntity("ComplianceIncident");
        model.IsEvent(x => x.OccurredOn);
    }
}
