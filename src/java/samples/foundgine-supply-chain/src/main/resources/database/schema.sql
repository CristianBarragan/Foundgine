CREATE TABLE IF NOT EXISTS suppliers (
  supplier_id   INT PRIMARY KEY,
  supplier_name VARCHAR(100) NOT NULL,
  country       VARCHAR(2)   NOT NULL,
  risk_score    NUMERIC(5,2) NOT NULL,
  tenant_id     VARCHAR(50)  NOT NULL);
CREATE TABLE IF NOT EXISTS warehouses (
  warehouse_id     INT PRIMARY KEY,
  business_unit_id INT NOT NULL,
  warehouse_name   VARCHAR(100) NOT NULL,
  tenant_id        VARCHAR(50)  NOT NULL);
CREATE TABLE IF NOT EXISTS products (
  product_id   INT PRIMARY KEY,
  sku          VARCHAR(50)  NOT NULL UNIQUE,
  product_name VARCHAR(100) NOT NULL,
  category     VARCHAR(50)  NOT NULL,
  safety_stock NUMERIC(10,2) NOT NULL);
