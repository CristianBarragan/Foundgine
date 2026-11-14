-- Foundgine migration InitialCreate (version 20261114161434)
-- Generated from the fluent model. Edit it freely until it has been applied;
-- after that Flyway rejects any change to this file.

CREATE TABLE "products" (
    "product_id" integer NOT NULL,
    "sku" varchar(50) NOT NULL,
    "product_name" varchar(100) NOT NULL,
    "category" varchar(50) NOT NULL,
    "safety_stock" numeric(10,2) NOT NULL,
    CONSTRAINT "pk_products" PRIMARY KEY ("product_id")
);
CREATE UNIQUE INDEX "ux_products_sku" ON "products" ("sku");

CREATE TABLE "suppliers" (
    "supplier_id" integer NOT NULL,
    "supplier_name" varchar(100) NOT NULL,
    "country" varchar(2) NOT NULL,
    "risk_score" numeric(5,2) NOT NULL,
    "tenant_id" varchar(50) NOT NULL,
    CONSTRAINT "pk_suppliers" PRIMARY KEY ("supplier_id")
);
CREATE INDEX "ix_suppliers_tenant_id" ON "suppliers" ("tenant_id");

CREATE TABLE "warehouses" (
    "warehouse_id" integer NOT NULL,
    "business_unit_id" integer NOT NULL,
    "warehouse_name" varchar(100) NOT NULL,
    "tenant_id" varchar(50) NOT NULL,
    CONSTRAINT "pk_warehouses" PRIMARY KEY ("warehouse_id")
);
CREATE INDEX "ix_warehouses_tenant_id" ON "warehouses" ("tenant_id");
