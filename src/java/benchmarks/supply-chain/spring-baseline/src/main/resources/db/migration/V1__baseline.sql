-- Hand-authored baseline schema for the Spring GraphQL + plain JPA/Hibernate arm.
-- Deliberately independent of Foundgine's migration generator. Keep this schema
-- structurally equivalent to the Foundgine JPA model and the benchmark seed SQL.

CREATE TABLE products (
    product_id integer NOT NULL,
    sku varchar(50) NOT NULL,
    product_name varchar(100) NOT NULL,
    category varchar(50) NOT NULL,
    safety_stock numeric(10,2) NOT NULL,
    CONSTRAINT pk_products PRIMARY KEY (product_id)
);
CREATE UNIQUE INDEX ux_products_sku ON products (sku);

CREATE TABLE suppliers (
    supplier_id integer NOT NULL,
    supplier_name varchar(100) NOT NULL,
    country varchar(2) NOT NULL,
    risk_score numeric(5,2) NOT NULL,
    tenant_id varchar(50) NOT NULL,
    CONSTRAINT pk_suppliers PRIMARY KEY (supplier_id)
);
CREATE INDEX ix_suppliers_tenant_id ON suppliers (tenant_id);

CREATE TABLE warehouses (
    warehouse_id integer NOT NULL,
    business_unit_id integer NOT NULL,
    warehouse_name varchar(100) NOT NULL,
    tenant_id varchar(50) NOT NULL,
    CONSTRAINT pk_warehouses PRIMARY KEY (warehouse_id)
);
CREATE INDEX ix_warehouses_tenant_id ON warehouses (tenant_id);

CREATE TABLE foundgine_warrant_revocation_epoch (singleton_id smallint PRIMARY KEY CHECK (singleton_id = 1), high_water_mark bigint NOT NULL CHECK (high_water_mark >= 0));
INSERT INTO foundgine_warrant_revocation_epoch(singleton_id, high_water_mark) VALUES (1, 0);
