CREATE TABLE IF NOT EXISTS customers (
  customer_id INT PRIMARY KEY,
  customer_name VARCHAR(120) NOT NULL,
  tenant_id VARCHAR(80) NOT NULL
);
CREATE TABLE IF NOT EXISTS products (
  product_id INT PRIMARY KEY,
  sku VARCHAR(50) NOT NULL UNIQUE,
  product_name VARCHAR(120) NOT NULL,
  category VARCHAR(80) NOT NULL,
  safety_stock NUMERIC(18,2) NOT NULL,
  unit_price NUMERIC(18,2) NOT NULL CHECK (unit_price >= 0)
);
CREATE TABLE IF NOT EXISTS warehouses (
  warehouse_id INT PRIMARY KEY,
  warehouse_name VARCHAR(120) NOT NULL,
  business_unit_id INT NOT NULL,
  tenant_id VARCHAR(80) NOT NULL
);
CREATE TABLE IF NOT EXISTS inventory_lots (
  lot_id INT PRIMARY KEY,
  warehouse_id INT NOT NULL REFERENCES warehouses(warehouse_id),
  product_id INT NOT NULL REFERENCES products(product_id),
  on_hand NUMERIC(18,2) NOT NULL CHECK (on_hand >= 0),
  reserved NUMERIC(18,2) NOT NULL DEFAULT 0 CHECK (reserved >= 0),
  quarantined NUMERIC(18,2) NOT NULL DEFAULT 0 CHECK (quarantined >= 0),
  received_on DATE NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_inventory_lots_product_warehouse ON inventory_lots(product_id, warehouse_id);
CREATE TABLE IF NOT EXISTS orders (
  order_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  customer_id INT NOT NULL REFERENCES customers(customer_id),
  status VARCHAR(30) NOT NULL CHECK (status IN ('Pending','Cancelled','Processing','Shipped','Delivered')),
  total_amount NUMERIC(18,2) NOT NULL,
  placed_on DATE NOT NULL
);
CREATE TABLE IF NOT EXISTS order_items (
  order_item_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  order_id INT NOT NULL REFERENCES orders(order_id) ON DELETE CASCADE,
  product_id INT NOT NULL REFERENCES products(product_id),
  quantity INT NOT NULL CHECK (quantity > 0),
  unit_price NUMERIC(18,2) NOT NULL
);
CREATE TABLE IF NOT EXISTS order_allocations (
  order_item_id INT PRIMARY KEY REFERENCES order_items(order_item_id) ON DELETE CASCADE,
  lot_id INT NOT NULL REFERENCES inventory_lots(lot_id),
  quantity INT NOT NULL CHECK (quantity > 0)
);
CREATE TABLE IF NOT EXISTS supply_chain_idempotency (
  idempotency_key VARCHAR(200) PRIMARY KEY,
  actor VARCHAR(200) NOT NULL,
  customer_id INT NOT NULL REFERENCES customers(customer_id),
  order_id INT NOT NULL REFERENCES orders(order_id),
  request_fingerprint CHAR(64) NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS supply_chain_cancellation_idempotency (
  idempotency_key VARCHAR(200) PRIMARY KEY,
  actor VARCHAR(200) NOT NULL,
  order_id INT NOT NULL REFERENCES orders(order_id),
  request_fingerprint CHAR(64) NOT NULL,
  restored_quantity INT NOT NULL,
  cancelled_on DATE NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_orders_customer_status ON orders(customer_id, status);
