using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Advanced.Domain;

namespace Foundgine.SupplyChain.Advanced.Semantics.Configuration;

// Provider-independent storage metadata: tables, keys and indexes.

public sealed class CompanyPersistenceConfiguration : FoundginePersistenceConfiguration<Company>
{
    public override void Configure(FoundginePersistenceBuilder<Company> model)
    {
        model.ToTable("companies");
        model.HasKey(x => x.Id);
    }
}

public sealed class BusinessUnitPersistenceConfiguration : FoundginePersistenceConfiguration<BusinessUnit>
{
    public override void Configure(FoundginePersistenceBuilder<BusinessUnit> model)
    {
        model.ToTable("business_units");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.CompanyId);
    }
}

public sealed class WarehousePersistenceConfiguration : FoundginePersistenceConfiguration<Warehouse>
{
    public override void Configure(FoundginePersistenceBuilder<Warehouse> model)
    {
        model.ToTable("warehouses");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.BusinessUnitId);
    }
}

public sealed class SupplierPersistenceConfiguration : FoundginePersistenceConfiguration<Supplier>
{
    public override void Configure(FoundginePersistenceBuilder<Supplier> model)
    {
        model.ToTable("suppliers");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.Country);
    }
}

public sealed class SupplierSitePersistenceConfiguration : FoundginePersistenceConfiguration<SupplierSite>
{
    public override void Configure(FoundginePersistenceBuilder<SupplierSite> model)
    {
        model.ToTable("supplier_sites");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.SupplierId);
    }
}

public sealed class SupplierCertificationPersistenceConfiguration : FoundginePersistenceConfiguration<SupplierCertification>
{
    public override void Configure(FoundginePersistenceBuilder<SupplierCertification> model)
    {
        model.ToTable("supplier_certifications");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.SupplierId);
    }
}

public sealed class ProductPersistenceConfiguration : FoundginePersistenceConfiguration<Product>
{
    public override void Configure(FoundginePersistenceBuilder<Product> model)
    {
        model.ToTable("products");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.Category);
    }
}

public sealed class ProductComponentPersistenceConfiguration : FoundginePersistenceConfiguration<ProductComponent>
{
    public override void Configure(FoundginePersistenceBuilder<ProductComponent> model)
    {
        model.ToTable("product_components");
        model.HasKey(x => x.ParentProductId);
        model.HasIndex(x => x.ComponentProductId);
    }
}

public sealed class PurchaseOrderPersistenceConfiguration : FoundginePersistenceConfiguration<PurchaseOrder>
{
    public override void Configure(FoundginePersistenceBuilder<PurchaseOrder> model)
    {
        model.ToTable("purchase_orders");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.SupplierId);
        model.HasIndex(x => x.WarehouseId);
    }
}

public sealed class PurchaseOrderLinePersistenceConfiguration : FoundginePersistenceConfiguration<PurchaseOrderLine>
{
    public override void Configure(FoundginePersistenceBuilder<PurchaseOrderLine> model)
    {
        model.ToTable("purchase_order_lines");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.ProductId);
    }
}

public sealed class ShipmentPersistenceConfiguration : FoundginePersistenceConfiguration<Shipment>
{
    public override void Configure(FoundginePersistenceBuilder<Shipment> model)
    {
        model.ToTable("shipments");
        model.HasKey(x => x.Id);
    }
}

public sealed class InventoryLotPersistenceConfiguration : FoundginePersistenceConfiguration<InventoryLot>
{
    public override void Configure(FoundginePersistenceBuilder<InventoryLot> model)
    {
        model.ToTable("inventory");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.ProductId);
    }
}

public sealed class CustomerOrderPersistenceConfiguration : FoundginePersistenceConfiguration<CustomerOrder>
{
    public override void Configure(FoundginePersistenceBuilder<CustomerOrder> model)
    {
        model.ToTable("customer_orders");
        model.HasKey(x => x.Id);
    }
}

public sealed class CustomerOrderLinePersistenceConfiguration : FoundginePersistenceConfiguration<CustomerOrderLine>
{
    public override void Configure(FoundginePersistenceBuilder<CustomerOrderLine> model)
    {
        model.ToTable("customer_order_lines");
        model.HasKey(x => x.Id);
    }
}

public sealed class InventoryMovementPersistenceConfiguration : FoundginePersistenceConfiguration<InventoryMovement>
{
    public override void Configure(FoundginePersistenceBuilder<InventoryMovement> model)
    {
        model.ToTable("inventory_movements");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.LotId);
    }
}

public sealed class ProductionOutputPersistenceConfiguration : FoundginePersistenceConfiguration<ProductionOutput>
{
    public override void Configure(FoundginePersistenceBuilder<ProductionOutput> model)
    {
        model.ToTable("production_outputs");
        model.HasKey(x => x.Id);
    }
}

public sealed class ComplianceIncidentPersistenceConfiguration : FoundginePersistenceConfiguration<ComplianceIncident>
{
    public override void Configure(FoundginePersistenceBuilder<ComplianceIncident> model)
    {
        model.ToTable("compliance_incidents");
        model.HasKey(x => x.Id);
        model.HasIndex(x => x.SupplierId);
    }
}
