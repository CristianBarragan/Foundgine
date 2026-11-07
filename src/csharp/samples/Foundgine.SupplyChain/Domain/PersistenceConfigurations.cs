using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Storage;

namespace Foundgine.SupplyChain.Domain;

// Persistence configuration: table, key and column names only.

internal sealed class CustomerERPConfiguration : FoundginePersistenceConfiguration<CustomerERP>
{
    public override void Configure(FoundginePersistenceBuilder<CustomerERP> model)
    {
        model.ToTable("customers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("customer_id");
        model.Property(x => x.FirstName).HasColumnName("first_name");
        model.Property(x => x.LastName).HasColumnName("last_name");
        model.Property(x => x.Email).HasColumnName("email");
    }
}

internal sealed class SalesOrderERPConfiguration : FoundginePersistenceConfiguration<SalesOrderERP>
{
    public override void Configure(FoundginePersistenceBuilder<SalesOrderERP> model)
    {
        model.ToTable("orders").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("order_id");
        model.Property(x => x.CustomerId).HasColumnName("customer_id");
        model.Property(x => x.Status).HasColumnName("status");
        model.Property(x => x.TotalAmount).HasColumnName("total_amount");
    }
}

internal sealed class SalesOrderLineERPConfiguration : FoundginePersistenceConfiguration<SalesOrderLineERP>
{
    public override void Configure(FoundginePersistenceBuilder<SalesOrderLineERP> model)
    {
        model.ToTable("order_items").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("order_item_id");
        model.Property(x => x.OrderId).HasColumnName("order_id");
        model.Property(x => x.ProductId).HasColumnName("product_id");
        model.Property(x => x.Quantity).HasColumnName("quantity");
        model.Property(x => x.UnitPrice).HasColumnName("unit_price");
    }
}

internal sealed class CatalogProductERPConfiguration : FoundginePersistenceConfiguration<CatalogProductERP>
{
    public override void Configure(FoundginePersistenceBuilder<CatalogProductERP> model)
    {
        model.ToTable("products").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("product_id");
        model.Property(x => x.Name).HasColumnName("product_name");
        model.Property(x => x.Sku).HasColumnName("sku");
        model.Property(x => x.UnitPrice).HasColumnName("unit_price");
        model.Property(x => x.SupplierId).HasColumnName("supplier_id");
        model.Property(x => x.CategoryId).HasColumnName("category_id");
    }
}

internal sealed class SupplierERPConfiguration : FoundginePersistenceConfiguration<SupplierERP>
{
    public override void Configure(FoundginePersistenceBuilder<SupplierERP> model)
    {
        model.ToTable("suppliers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("supplier_id");
        model.Property(x => x.Name).HasColumnName("supplier_name");
        model.Property(x => x.Email).HasColumnName("email");
    }
}

internal sealed class CategoryERPConfiguration : FoundginePersistenceConfiguration<CategoryERP>
{
    public override void Configure(FoundginePersistenceBuilder<CategoryERP> model)
    {
        model.ToTable("categories").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("category_id");
        model.Property(x => x.Name).HasColumnName("category_name");
    }
}

internal sealed class InventoryPositionERPConfiguration : FoundginePersistenceConfiguration<InventoryPositionERP>
{
    public override void Configure(FoundginePersistenceBuilder<InventoryPositionERP> model)
    {
        model.ToTable("inventory").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("inventory_id");
        model.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
        model.Property(x => x.ProductId).HasColumnName("product_id");
        model.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand");
        model.Property(x => x.ReorderLevel).HasColumnName("reorder_level");
    }
}

internal sealed class WarehouseERPConfiguration : FoundginePersistenceConfiguration<WarehouseERP>
{
    public override void Configure(FoundginePersistenceBuilder<WarehouseERP> model)
    {
        model.ToTable("warehouses").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("warehouse_id");
        model.Property(x => x.Name).HasColumnName("warehouse_name");
        model.Property(x => x.Location).HasColumnName("location");
    }
}

internal sealed class ShipmentERPConfiguration : FoundginePersistenceConfiguration<ShipmentERP>
{
    public override void Configure(FoundginePersistenceBuilder<ShipmentERP> model)
    {
        model.ToTable("shipments").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("shipment_id");
        model.Property(x => x.OrderId).HasColumnName("order_id");
        model.Property(x => x.CarrierId).HasColumnName("carrier_id");
        model.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
        model.Property(x => x.TrackingNumber).HasColumnName("tracking_number");
        model.Property(x => x.Status).HasColumnName("shipping_status");
    }
}

internal sealed class CarrierERPConfiguration : FoundginePersistenceConfiguration<CarrierERP>
{
    public override void Configure(FoundginePersistenceBuilder<CarrierERP> model)
    {
        model.ToTable("carriers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("carrier_id");
        model.Property(x => x.Name).HasColumnName("carrier_name");
    }
}
