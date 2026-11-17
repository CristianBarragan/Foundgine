-- Benchmark data for the C# group. Applied by the HARNESS (psql) after each arm's own migrations, identically for both arms,
-- so no arm's data depends on another arm's tooling. Never touches the migration history tables.
TRUNCATE shipments, order_items, orders, inventory, products, carriers, warehouses, categories, suppliers, customers RESTART IDENTITY CASCADE;
INSERT INTO customers (customer_id, first_name, last_name, email) OVERRIDING SYSTEM VALUE VALUES
  (1,'Alice','Anders','alice@example.com'),(2,'Bob','Brandt','bob@example.com'),(3,'Cara','Cole','cara@example.com');
INSERT INTO suppliers (supplier_id, supplier_name, email) OVERRIDING SYSTEM VALUE VALUES (1,'Acme Components','sales@acme.example'),(2,'Borealis Metals','sales@borealis.example');
INSERT INTO categories (category_id, category_name) OVERRIDING SYSTEM VALUE VALUES (1,'Hardware'),(2,'Fasteners');
INSERT INTO products (product_id, product_name, sku, unit_price, supplier_id, category_id) OVERRIDING SYSTEM VALUE
  SELECT n, 'Product ' || n, 'SKU-' || (100 * n), 1.25 * n + 10, 1 + n % 2, 1 + n % 2 FROM generate_series(1, 20) n;
INSERT INTO warehouses (warehouse_id, warehouse_name, location) OVERRIDING SYSTEM VALUE VALUES (1,'Austin DC','Austin, TX'),(2,'Rotterdam DC','Rotterdam, NL');
INSERT INTO carriers (carrier_id, carrier_name) OVERRIDING SYSTEM VALUE VALUES (1,'FastFreight');
INSERT INTO inventory (inventory_id, warehouse_id, product_id, quantity_on_hand, reorder_level) OVERRIDING SYSTEM VALUE
  SELECT (w - 1) * 20 + p, w, p, 10 * p, 20 FROM generate_series(1, 2) w, generate_series(1, 20) p;
-- 100 orders alternating alice/bob (50 each: `first: 50` returns a full page for a customer), 2 lines and 1 shipment per order.
INSERT INTO orders (order_id, customer_id, status, total_amount) OVERRIDING SYSTEM VALUE
  SELECT n, 1 + n % 2, CASE WHEN n % 3 = 0 THEN 'Shipped' ELSE 'Pending' END, 70.00 FROM generate_series(1, 100) n;
INSERT INTO order_items (order_item_id, order_id, product_id, quantity, unit_price) OVERRIDING SYSTEM VALUE
  SELECT n * 2 - 1 + l, n, 1 + l, 1 + l, 10.00 + 40 * l FROM generate_series(1, 100) n, generate_series(0, 1) l;
INSERT INTO shipments (shipment_id, order_id, carrier_id, warehouse_id, tracking_number, shipping_status) OVERRIDING SYSTEM VALUE
  SELECT n, n, 1, 1 + n % 2, 'TRK-' || lpad(n::text, 6, '0'), 'In Transit' FROM generate_series(1, 100) n;
SELECT setval(pg_get_serial_sequence(t, c), (SELECT 1000), false) FROM (VALUES
  ('customers','customer_id'),('suppliers','supplier_id'),('categories','category_id'),('products','product_id'),('warehouses','warehouse_id'),
  ('carriers','carrier_id'),('inventory','inventory_id'),('orders','order_id'),('order_items','order_item_id'),('shipments','shipment_id')) v(t, c);
