using Foundgine.SupplyChain.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Foundgine.SupplyChain.Domain;

/// <summary>
/// EF Core storage details that Foundgine's persistence metadata does not carry: lengths, precision, check
/// constraints, indexes, foreign keys and the two EF-only tables used by the order workflow. Applied after the
/// metadata-derived model (see <c>SupplyChainEf.ConfigureModel</c>), so table, key and column names stay in
/// <c>PersistenceConfigurations.cs</c>. The migration in <c>Migrations/</c> is generated from the same facts
/// (<c>tools/gen_migration.py</c>).
/// </summary>
public static class SupplyChainStorage
{
    public static void Apply(ModelBuilder mb)
    {
        mb.Entity<Customer>(e =>
        {
            e.HasKey(x => x.Id).HasName("customers_pkey");
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(320);
            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_customers_email");
        });

        mb.Entity<Supplier>(e =>
        {
            e.HasKey(x => x.Id).HasName("suppliers_pkey");
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(320);
        });

        mb.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id).HasName("categories_pkey");
            e.Property(x => x.Name).HasMaxLength(100);
        });

        mb.Entity<Warehouse>(e =>
        {
            e.HasKey(x => x.Id).HasName("warehouses_pkey");
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Location).HasMaxLength(200);
        });

        mb.Entity<Carrier>(e =>
        {
            e.HasKey(x => x.Id).HasName("carriers_pkey");
            e.Property(x => x.Name).HasMaxLength(100);
        });

        mb.Entity<CatalogProduct>(e =>
        {
            e.ToTable("products", t => t.HasCheckConstraint("products_unit_price_check", "unit_price >= 0"));
            e.HasKey(x => x.Id).HasName("products_pkey");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Sku).HasMaxLength(64);
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("ux_products_sku");
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId)
                .HasConstraintName("products_supplier_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId)
                .HasConstraintName("products_category_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<InventoryPosition>(e =>
        {
            e.ToTable("inventory", t =>
            {
                t.HasCheckConstraint("inventory_quantity_check", "quantity_on_hand >= 0");
                t.HasCheckConstraint("inventory_reorder_check", "reorder_level >= 0");
            });
            e.HasKey(x => x.Id).HasName("inventory_pkey");
            // Not part of the semantic model: maintained by the order workflow (native SQL) and by the database.
            e.Property<DateTime>("LastUpdated").HasColumnName("last_updated").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasIndex(x => new { x.WarehouseId, x.ProductId }).IsUnique().HasDatabaseName("ux_inventory_warehouse_product");
            e.HasIndex(x => x.ProductId).HasDatabaseName("ix_inventory_product");
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
                .HasConstraintName("inventory_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CatalogProduct>().WithMany().HasForeignKey(x => x.ProductId)
                .HasConstraintName("inventory_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<SalesOrder>(e =>
        {
            e.ToTable("orders", t => t.HasCheckConstraint("orders_status_check",
                "status IN ('Pending','Cancelled','Shipped','Delivered')"));
            e.HasKey(x => x.Id).HasName("orders_pkey");
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.TotalAmount).HasPrecision(14, 2);
            e.HasIndex(x => x.CustomerId).HasDatabaseName("ix_orders_customer");
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
                .HasConstraintName("orders_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<SalesOrderLine>(e =>
        {
            e.ToTable("order_items", t =>
            {
                t.HasCheckConstraint("order_items_quantity_check", "quantity > 0");
                t.HasCheckConstraint("order_items_unit_price_check", "unit_price >= 0");
            });
            e.HasKey(x => x.Id).HasName("order_items_pkey");
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.HasIndex(x => x.OrderId).HasDatabaseName("ix_order_items_order");
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId)
                .HasConstraintName("order_items_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CatalogProduct>().WithMany().HasForeignKey(x => x.ProductId)
                .HasConstraintName("order_items_product_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Shipment>(e =>
        {
            e.ToTable("shipments", t => t.HasCheckConstraint("shipments_status_check",
                "shipping_status IN ('In Transit','Out for Delivery','Delivered','Delayed')"));
            e.HasKey(x => x.Id).HasName("shipments_pkey");
            e.Property(x => x.TrackingNumber).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(30);
            e.HasIndex(x => x.OrderId).HasDatabaseName("ix_shipments_order");
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId)
                .HasConstraintName("shipments_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Carrier>().WithMany().HasForeignKey(x => x.CarrierId)
                .HasConstraintName("shipments_carrier_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
                .HasConstraintName("shipments_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        // ---- EF-only tables (not part of the semantic model; written by native SQL in OrderWorkflows) ------------

        mb.Entity<OrderAllocation>(e =>
        {
            e.ToTable("order_allocations", t => t.HasCheckConstraint("order_allocations_quantity_check", "quantity > 0"));
            e.HasKey(x => new { x.OrderItemId, x.WarehouseId }).HasName("order_allocations_pkey");
            e.Property(x => x.OrderItemId).HasColumnName("order_item_id");
            e.Property(x => x.WarehouseId).HasColumnName("warehouse_id");
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => x.OrderItemId)
                .HasConstraintName("order_allocations_order_item_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
                .HasConstraintName("order_allocations_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("supply_chain_idempotency");
            e.HasKey(x => x.IdempotencyKey).HasName("supply_chain_idempotency_pkey");
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).ValueGeneratedNever();
            e.Property(x => x.ActorId).HasColumnName("actor_id");
            e.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(50);
            e.Property(x => x.OrderId).HasColumnName("order_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.OrderId)
                .HasConstraintName("supply_chain_idempotency_order_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });
    }
}

/// <summary>One allocation of stock to an order line (EF-only table <c>order_allocations</c>).</summary>
public sealed class OrderAllocation
{
    public int OrderItemId { get; init; }
    public int WarehouseId { get; init; }
    public int Quantity { get; init; }
}

/// <summary>Idempotency record for <c>place_order</c> (EF-only table <c>supply_chain_idempotency</c>).</summary>
public sealed class IdempotencyRecord
{
    public string IdempotencyKey { get; init; } = "";
    public int ActorId { get; init; }
    public string Operation { get; init; } = "";
    public int OrderId { get; init; }
    public DateTime CreatedAt { get; init; }
}
