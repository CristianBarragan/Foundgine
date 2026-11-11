using Foundgine.Core.Semantic.Configuration;
using Foundgine.SupplyChain.Domain.Models;

namespace Foundgine.SupplyChain.Domain;

// Persistence configuration: table, key and column names only. Applied to the domain classes themselves
// (they are the EF entities); the provider derives the EF model from this.

internal sealed class CustomerTable : FoundginePersistenceConfiguration<Customer>
{
    public override void Configure(FoundginePersistenceBuilder<Customer> model)
    {
        model.ToTable("customers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("customer_id");
        model.Property(x => x.FirstName).HasColumnName("first_name");
        model.Property(x => x.LastName).HasColumnName("last_name");
        model.Property(x => x.Email).HasColumnName("email");
    }
}

internal sealed class SalesOrderTable : FoundginePersistenceConfiguration<SalesOrder>
{
    public override void Configure(FoundginePersistenceBuilder<SalesOrder> model)
    {
        model.ToTable("orders").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("order_id");
        model.Property(x => x.CustomerId).HasColumnName("customer_id");
        model.Property(x => x.Status).HasColumnName("status");
        model.Property(x => x.TotalAmount).HasColumnName("total_amount");
    }
}

internal sealed class SalesOrderLineTable : FoundginePersistenceConfiguration<SalesOrderLine>
{
    public override void Configure(FoundginePersistenceBuilder<SalesOrderLine> model)
    {
        model.ToTable("order_items").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("order_item_id");
        model.Property(x => x.OrderId).HasColumnName("order_id");
        model.Property(x => x.ProductId).HasColumnName("product_id");
        model.Property(x => x.Quantity).HasColumnName("quantity");
        model.Property(x => x.UnitPrice).HasColumnName("unit_price");
    }
}

internal sealed class CatalogProductTable : FoundginePersistenceConfiguration<CatalogProduct>
{
    public override void Configure(FoundginePersistenceBuilder<CatalogProduct> model)
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

internal sealed class SupplierTable : FoundginePersistenceConfiguration<Supplier>
{
    public override void Configure(FoundginePersistenceBuilder<Supplier> model)
    {
        model.ToTable("suppliers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("supplier_id");
        model.Property(x => x.Name).HasColumnName("supplier_name");
        model.Property(x => x.Email).HasColumnName("email");
    }
}

internal sealed class CategoryTable : FoundginePersistenceConfiguration<Category>
{
    public override void Configure(FoundginePersistenceBuilder<Category> model)
    {
        model.ToTable("categories").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("category_id");
        model.Property(x => x.Name).HasColumnName("category_name");
    }
}

internal sealed class InventoryPositionTable : FoundginePersistenceConfiguration<InventoryPosition>
{
    public override void Configure(FoundginePersistenceBuilder<InventoryPosition> model)
    {
        model.ToTable("inventory").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("inventory_id");
        model.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
        model.Property(x => x.ProductId).HasColumnName("product_id");
        model.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand");
        model.Property(x => x.ReorderLevel).HasColumnName("reorder_level");
    }
}

internal sealed class WarehouseTable : FoundginePersistenceConfiguration<Warehouse>
{
    public override void Configure(FoundginePersistenceBuilder<Warehouse> model)
    {
        model.ToTable("warehouses").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("warehouse_id");
        model.Property(x => x.Name).HasColumnName("warehouse_name");
        model.Property(x => x.Location).HasColumnName("location");
    }
}

internal sealed class ShipmentTable : FoundginePersistenceConfiguration<Shipment>
{
    public override void Configure(FoundginePersistenceBuilder<Shipment> model)
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

internal sealed class CarrierTable : FoundginePersistenceConfiguration<Carrier>
{
    public override void Configure(FoundginePersistenceBuilder<Carrier> model)
    {
        model.ToTable("carriers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("carrier_id");
        model.Property(x => x.Name).HasColumnName("carrier_name");
    }
}
