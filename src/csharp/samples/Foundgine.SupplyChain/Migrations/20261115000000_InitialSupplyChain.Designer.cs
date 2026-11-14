using System;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Foundgine.SupplyChain.Migrations;

[DbContext(typeof(FoundgineDbContext))]
[Migration("20261115000000_InitialSupplyChain")]
partial class InitialSupplyChain
{
    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "10.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Customer", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("customer_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("FirstName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("first_name");
                b.Property<string>("LastName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("last_name");
                b.Property<string>("Email").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)").HasColumnName("email");
                b.HasKey("Id").HasName("customers_pkey");
                b.HasIndex(new[] { "Email" }, "ux_customers_email").IsUnique();
                b.ToTable("customers");
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Supplier", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("supplier_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("supplier_name");
                b.Property<string>("Email").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)").HasColumnName("email");
                b.HasKey("Id").HasName("suppliers_pkey");
                b.ToTable("suppliers");
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Category", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("category_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("category_name");
                b.HasKey("Id").HasName("categories_pkey");
                b.ToTable("categories");
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Warehouse", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("warehouse_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("warehouse_name");
                b.Property<string>("Location").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("location");
                b.HasKey("Id").HasName("warehouses_pkey");
                b.ToTable("warehouses");
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Carrier", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("carrier_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("carrier_name");
                b.HasKey("Id").HasName("carriers_pkey");
                b.ToTable("carriers");
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.CatalogProduct", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("product_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("product_name");
                b.Property<string>("Sku").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("sku");
                b.Property<decimal>("UnitPrice").HasPrecision(12, 2).HasColumnType("numeric(12,2)").HasColumnName("unit_price");
                b.Property<int>("SupplierId").HasColumnType("integer").HasColumnName("supplier_id");
                b.Property<int>("CategoryId").HasColumnType("integer").HasColumnName("category_id");
                b.HasKey("Id").HasName("products_pkey");
                b.HasIndex(new[] { "Sku" }, "ux_products_sku").IsUnique();
                b.ToTable("products", t =>
                {
                    t.HasCheckConstraint("products_unit_price_check", "unit_price >= 0");
                });
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.InventoryPosition", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("inventory_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<int>("WarehouseId").HasColumnType("integer").HasColumnName("warehouse_id");
                b.Property<int>("ProductId").HasColumnType("integer").HasColumnName("product_id");
                b.Property<int>("QuantityOnHand").HasColumnType("integer").HasColumnName("quantity_on_hand");
                b.Property<int>("ReorderLevel").HasColumnType("integer").HasColumnName("reorder_level");
                b.Property<DateTime>("LastUpdated").HasColumnType("timestamp with time zone").HasColumnName("last_updated").HasDefaultValueSql("CURRENT_TIMESTAMP");
                b.HasKey("Id").HasName("inventory_pkey");
                b.HasIndex(new[] { "WarehouseId", "ProductId" }, "ux_inventory_warehouse_product").IsUnique();
                b.HasIndex(new[] { "ProductId" }, "ix_inventory_product");
                b.ToTable("inventory", t =>
                {
                    t.HasCheckConstraint("inventory_quantity_check", "quantity_on_hand >= 0");
                    t.HasCheckConstraint("inventory_reorder_check", "reorder_level >= 0");
                });
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.SalesOrder", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("order_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<int>("CustomerId").HasColumnType("integer").HasColumnName("customer_id");
                b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
                b.Property<decimal>("TotalAmount").HasPrecision(14, 2).HasColumnType("numeric(14,2)").HasColumnName("total_amount");
                b.HasKey("Id").HasName("orders_pkey");
                b.HasIndex(new[] { "CustomerId" }, "ix_orders_customer");
                b.ToTable("orders", t =>
                {
                    t.HasCheckConstraint("orders_status_check", "status IN ('Pending','Cancelled','Shipped','Delivered')");
                });
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.SalesOrderLine", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("order_item_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<int>("OrderId").HasColumnType("integer").HasColumnName("order_id");
                b.Property<int>("ProductId").HasColumnType("integer").HasColumnName("product_id");
                b.Property<int>("Quantity").HasColumnType("integer").HasColumnName("quantity");
                b.Property<decimal>("UnitPrice").HasPrecision(12, 2).HasColumnType("numeric(12,2)").HasColumnName("unit_price");
                b.HasKey("Id").HasName("order_items_pkey");
                b.HasIndex(new[] { "OrderId" }, "ix_order_items_order");
                b.ToTable("order_items", t =>
                {
                    t.HasCheckConstraint("order_items_quantity_check", "quantity > 0");
                    t.HasCheckConstraint("order_items_unit_price_check", "unit_price >= 0");
                });
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Shipment", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("shipment_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<int>("OrderId").HasColumnType("integer").HasColumnName("order_id");
                b.Property<int>("CarrierId").HasColumnType("integer").HasColumnName("carrier_id");
                b.Property<int>("WarehouseId").HasColumnType("integer").HasColumnName("warehouse_id");
                b.Property<string>("TrackingNumber").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("tracking_number");
                b.Property<string>("Status").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)").HasColumnName("shipping_status");
                b.HasKey("Id").HasName("shipments_pkey");
                b.HasIndex(new[] { "OrderId" }, "ix_shipments_order");
                b.ToTable("shipments", t =>
                {
                    t.HasCheckConstraint("shipments_status_check", "shipping_status IN ('In Transit','Out for Delivery','Delivered','Delayed')");
                });
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.OrderAllocation", b =>
            {
                b.Property<int>("OrderItemId").HasColumnType("integer").HasColumnName("order_item_id");
                b.Property<int>("WarehouseId").HasColumnType("integer").HasColumnName("warehouse_id");
                b.Property<int>("Quantity").HasColumnType("integer").HasColumnName("quantity");
                b.HasKey("OrderItemId", "WarehouseId").HasName("order_allocations_pkey");
                b.ToTable("order_allocations", t =>
                {
                    t.HasCheckConstraint("order_allocations_quantity_check", "quantity > 0");
                });
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.IdempotencyRecord", b =>
            {
                b.Property<string>("IdempotencyKey").ValueGeneratedNever().IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("idempotency_key");
                b.Property<int>("ActorId").HasColumnType("integer").HasColumnName("actor_id");
                b.Property<string>("Operation").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)").HasColumnName("operation");
                b.Property<int>("OrderId").HasColumnType("integer").HasColumnName("order_id");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                b.HasKey("IdempotencyKey").HasName("supply_chain_idempotency_pkey");
                b.ToTable("supply_chain_idempotency");
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.CatalogProduct", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.Supplier", null).WithMany().HasForeignKey("SupplierId").HasConstraintName("products_supplier_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.CatalogProduct", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.Category", null).WithMany().HasForeignKey("CategoryId").HasConstraintName("products_category_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.InventoryPosition", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.Warehouse", null).WithMany().HasForeignKey("WarehouseId").HasConstraintName("inventory_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.InventoryPosition", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.CatalogProduct", null).WithMany().HasForeignKey("ProductId").HasConstraintName("inventory_product_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.SalesOrder", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.Customer", null).WithMany().HasForeignKey("CustomerId").HasConstraintName("orders_customer_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.SalesOrderLine", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.SalesOrder", null).WithMany().HasForeignKey("OrderId").HasConstraintName("order_items_order_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.SalesOrderLine", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.CatalogProduct", null).WithMany().HasForeignKey("ProductId").HasConstraintName("order_items_product_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Shipment", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.SalesOrder", null).WithMany().HasForeignKey("OrderId").HasConstraintName("shipments_order_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Shipment", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.Carrier", null).WithMany().HasForeignKey("CarrierId").HasConstraintName("shipments_carrier_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.Models.Shipment", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.Warehouse", null).WithMany().HasForeignKey("WarehouseId").HasConstraintName("shipments_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.OrderAllocation", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.SalesOrderLine", null).WithMany().HasForeignKey("OrderItemId").HasConstraintName("order_allocations_order_item_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.OrderAllocation", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.Warehouse", null).WithMany().HasForeignKey("WarehouseId").HasConstraintName("order_allocations_warehouse_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Foundgine.SupplyChain.Domain.IdempotencyRecord", b =>
            {
                b.HasOne("Foundgine.SupplyChain.Domain.Models.SalesOrder", null).WithMany().HasForeignKey("OrderId").HasConstraintName("supply_chain_idempotency_order_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

#pragma warning restore 612, 618
    }
}
