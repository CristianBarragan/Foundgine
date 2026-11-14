using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Foundgine.SupplyChain.Migrations;

/// <summary>Initial supply-chain schema for PostgreSQL. Generated from tools/gen_migration.py.</summary>
public partial class InitialSupplyChain : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    customer_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("customers_pkey", x => x.customer_id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_customers_email",
                table: "customers",
                column: "email", unique: true);

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    supplier_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("suppliers_pkey", x => x.supplier_id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    category_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("categories_pkey", x => x.category_id);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    warehouse_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    warehouse_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouses_pkey", x => x.warehouse_id);
                });

            migrationBuilder.CreateTable(
                name: "carriers",
                columns: table => new
                {
                    carrier_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    carrier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("carriers_pkey", x => x.carrier_id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    product_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    supplier_id = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("products_pkey", x => x.product_id);
                    table.ForeignKey(name: "products_supplier_id_fkey", column: x => x.supplier_id, principalTable: "suppliers", principalColumn: "supplier_id");
                    table.ForeignKey(name: "products_category_id_fkey", column: x => x.category_id, principalTable: "categories", principalColumn: "category_id");
                    table.CheckConstraint("products_unit_price_check", "unit_price >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_products_sku",
                table: "products",
                column: "sku", unique: true);

            migrationBuilder.CreateTable(
                name: "inventory",
                columns: table => new
                {
                    inventory_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    warehouse_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    reorder_level = table.Column<int>(type: "integer", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_pkey", x => x.inventory_id);
                    table.ForeignKey(name: "inventory_warehouse_id_fkey", column: x => x.warehouse_id, principalTable: "warehouses", principalColumn: "warehouse_id");
                    table.ForeignKey(name: "inventory_product_id_fkey", column: x => x.product_id, principalTable: "products", principalColumn: "product_id");
                    table.CheckConstraint("inventory_quantity_check", "quantity_on_hand >= 0");
                    table.CheckConstraint("inventory_reorder_check", "reorder_level >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_inventory_warehouse_product",
                table: "inventory",
                columns: new[] { "warehouse_id", "product_id" }, unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_product",
                table: "inventory",
                column: "product_id");

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    order_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("orders_pkey", x => x.order_id);
                    table.ForeignKey(name: "orders_customer_id_fkey", column: x => x.customer_id, principalTable: "customers", principalColumn: "customer_id");
                    table.CheckConstraint("orders_status_check", "status IN ('Pending','Cancelled','Shipped','Delivered')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer",
                table: "orders",
                column: "customer_id");

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    order_item_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_items_pkey", x => x.order_item_id);
                    table.ForeignKey(name: "order_items_order_id_fkey", column: x => x.order_id, principalTable: "orders", principalColumn: "order_id");
                    table.ForeignKey(name: "order_items_product_id_fkey", column: x => x.product_id, principalTable: "products", principalColumn: "product_id");
                    table.CheckConstraint("order_items_quantity_check", "quantity > 0");
                    table.CheckConstraint("order_items_unit_price_check", "unit_price >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    shipment_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    carrier_id = table.Column<int>(type: "integer", nullable: false),
                    warehouse_id = table.Column<int>(type: "integer", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shipping_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shipments_pkey", x => x.shipment_id);
                    table.ForeignKey(name: "shipments_order_id_fkey", column: x => x.order_id, principalTable: "orders", principalColumn: "order_id");
                    table.ForeignKey(name: "shipments_carrier_id_fkey", column: x => x.carrier_id, principalTable: "carriers", principalColumn: "carrier_id");
                    table.ForeignKey(name: "shipments_warehouse_id_fkey", column: x => x.warehouse_id, principalTable: "warehouses", principalColumn: "warehouse_id");
                    table.CheckConstraint("shipments_status_check", "shipping_status IN ('In Transit','Out for Delivery','Delivered','Delayed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_order",
                table: "shipments",
                column: "order_id");

            migrationBuilder.CreateTable(
                name: "order_allocations",
                columns: table => new
                {
                    order_item_id = table.Column<int>(type: "integer", nullable: false),
                    warehouse_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("order_allocations_pkey", x => new { x.order_item_id, x.warehouse_id });
                    table.ForeignKey(name: "order_allocations_order_item_id_fkey", column: x => x.order_item_id, principalTable: "order_items", principalColumn: "order_item_id");
                    table.ForeignKey(name: "order_allocations_warehouse_id_fkey", column: x => x.warehouse_id, principalTable: "warehouses", principalColumn: "warehouse_id");
                    table.CheckConstraint("order_allocations_quantity_check", "quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "supply_chain_idempotency",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_id = table.Column<int>(type: "integer", nullable: false),
                    operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("supply_chain_idempotency_pkey", x => x.idempotency_key);
                    table.ForeignKey(name: "supply_chain_idempotency_order_id_fkey", column: x => x.order_id, principalTable: "orders", principalColumn: "order_id");
                });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
            migrationBuilder.DropTable(name: "supply_chain_idempotency");
            migrationBuilder.DropTable(name: "order_allocations");
            migrationBuilder.DropTable(name: "shipments");
            migrationBuilder.DropTable(name: "order_items");
            migrationBuilder.DropTable(name: "orders");
            migrationBuilder.DropTable(name: "inventory");
            migrationBuilder.DropTable(name: "products");
            migrationBuilder.DropTable(name: "carriers");
            migrationBuilder.DropTable(name: "warehouses");
            migrationBuilder.DropTable(name: "categories");
            migrationBuilder.DropTable(name: "suppliers");
            migrationBuilder.DropTable(name: "customers");
    }
}
