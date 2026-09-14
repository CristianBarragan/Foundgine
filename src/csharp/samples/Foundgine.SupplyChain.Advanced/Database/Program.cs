using Npgsql;
using System.Text;

const string Schema = """
                      -- pg_trgm backs RetrievalStrategy.Fuzzy (see PostgresRetrievalCandidateSource
                      -- and SupplyChainExecutionService.TryFuzzyAsync): it's contrib, ships with
                      -- every stock PostgreSQL image, and needs no opt-in. pg_search (BM25,
                      -- RetrievalStrategy.Search) is deliberately NOT provisioned here - it's a
                      -- ParadeDB extension that isn't present on a vanilla image, so that path is
                      -- gated behind FOUNDGINE_POSTGRES_PGSEARCH=1 and degrades to "no candidates"
                      -- rather than failing when the extension is missing.
                      CREATE EXTENSION IF NOT EXISTS pg_trgm;
                      CREATE TABLE IF NOT EXISTS suppliers (supplier_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,supplier_name VARCHAR(100) NOT NULL,contact_name VARCHAR(100),email VARCHAR(100) UNIQUE,phone VARCHAR(20),address VARCHAR(255),state VARCHAR(2),total_order_value DECIMAL(14,2) NOT NULL DEFAULT 0,negotiated_cost DECIMAL(14,2),created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP);
                      CREATE INDEX IF NOT EXISTS ix_suppliers_name_trgm ON suppliers USING gin (supplier_name gin_trgm_ops);
                      CREATE TABLE IF NOT EXISTS purchase_orders (purchase_order_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,supplier_id INT NOT NULL REFERENCES suppliers(supplier_id),expected_date DATE NOT NULL,received_date DATE,status VARCHAR(20) NOT NULL DEFAULT 'Open' CHECK(status IN('Open','Received','Cancelled')));
                      CREATE INDEX IF NOT EXISTS ix_purchase_orders_supplier ON purchase_orders(supplier_id);
                      CREATE TABLE IF NOT EXISTS categories (category_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,category_name VARCHAR(50) NOT NULL UNIQUE,description TEXT);
                      CREATE TABLE IF NOT EXISTS products (product_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,product_name VARCHAR(100) NOT NULL,sku VARCHAR(50) UNIQUE NOT NULL,category_id INT REFERENCES categories(category_id),supplier_id INT REFERENCES suppliers(supplier_id),unit_price DECIMAL(10,2) NOT NULL,weight_kg DECIMAL(6,2),created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP);
                      CREATE TABLE IF NOT EXISTS warehouses (warehouse_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,warehouse_name VARCHAR(100) NOT NULL,location VARCHAR(255) NOT NULL,capacity_m3 INT);
                      CREATE TABLE IF NOT EXISTS inventory (inventory_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,warehouse_id INT REFERENCES warehouses(warehouse_id),product_id INT REFERENCES products(product_id),quantity_on_hand INT DEFAULT 0,reorder_level INT DEFAULT 10,last_updated TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,UNIQUE(warehouse_id,product_id));
                      CREATE TABLE IF NOT EXISTS customers (customer_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,first_name VARCHAR(50) NOT NULL,last_name VARCHAR(50) NOT NULL,email VARCHAR(100) UNIQUE NOT NULL,phone VARCHAR(20),shipping_address VARCHAR(255) NOT NULL);
                      CREATE TABLE IF NOT EXISTS orders (order_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,customer_id INT REFERENCES customers(customer_id),order_date TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,status VARCHAR(20) NOT NULL DEFAULT 'Pending' CHECK(status IN('Pending','Processing','Shipped','Delivered','Cancelled')),total_amount DECIMAL(12,2) NOT NULL);
                      CREATE TABLE IF NOT EXISTS order_items (order_item_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,order_id INT REFERENCES orders(order_id) ON DELETE CASCADE,product_id INT REFERENCES products(product_id),quantity INT NOT NULL CHECK(quantity>0),unit_price DECIMAL(10,2) NOT NULL);
                      CREATE TABLE IF NOT EXISTS order_allocations (order_allocation_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,order_item_id INT NOT NULL REFERENCES order_items(order_item_id) ON DELETE CASCADE,warehouse_id INT NOT NULL REFERENCES warehouses(warehouse_id),quantity INT NOT NULL CHECK(quantity>0),UNIQUE(order_item_id,warehouse_id));
                      CREATE TABLE IF NOT EXISTS carriers (carrier_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,carrier_name VARCHAR(100) NOT NULL,tracking_url_template VARCHAR(255),contact_phone VARCHAR(20));
                      CREATE TABLE IF NOT EXISTS shipments (shipment_id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,order_id INT REFERENCES orders(order_id),carrier_id INT REFERENCES carriers(carrier_id),warehouse_id INT REFERENCES warehouses(warehouse_id),tracking_number VARCHAR(100) UNIQUE,shipment_date TIMESTAMPTZ,estimated_delivery DATE,actual_delivery DATE,shipping_status VARCHAR(30) NOT NULL DEFAULT 'In Transit' CHECK(shipping_status IN('In Transit','Out for Delivery','Delivered','Delayed')));
                      CREATE TABLE IF NOT EXISTS supply_chain_idempotency (idempotency_key VARCHAR(200) PRIMARY KEY,actor_id INT NOT NULL,operation VARCHAR(100) NOT NULL,order_id INT,created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP);
                      CREATE INDEX IF NOT EXISTS ix_orders_customer_status ON orders(customer_id,status); CREATE INDEX IF NOT EXISTS ix_items_order ON order_items(order_id); CREATE INDEX IF NOT EXISTS ix_inventory_product ON inventory(product_id); CREATE INDEX IF NOT EXISTS ix_products_supplier ON products(supplier_id);
                      """;

var cs = Environment.GetEnvironmentVariable("SupplyChainConnectionString") ??
         throw new InvalidOperationException("SupplyChainConnectionString is required.");
var customers = Math.Clamp(GetInt("SUPPLY_CHAIN_CUSTOMERS", 5), 1, 5);
await using var dataSource = NpgsqlDataSource.Create(cs);
await using var db = await dataSource.OpenConnectionAsync();
await using var tx = await db.BeginTransactionAsync();
await using (var schema = new NpgsqlCommand(Schema, db, tx)) await schema.ExecuteNonQueryAsync();
await using (var clear =
             new NpgsqlCommand(
                 "TRUNCATE purchase_orders, order_allocations, order_items, shipments, orders, inventory, products, categories, suppliers, carriers, warehouses, customers RESTART IDENTITY CASCADE;",
                 db, tx)) await clear.ExecuteNonQueryAsync();

// Four suppliers, deliberately seeded so that ranking "top supplier" by
// state produces two different shapes of evidence:
//   - state=TX has a clear, unambiguous top supplier (Acme, 482000 > 210000)
//   - state=CA has a TIE at the top (Northstar and Southline both 300000)
//   - any other state (e.g. NY) has zero suppliers, for the not-found case
// negotiated_cost is a commercially sensitive field, mirroring
// Supplier.NegotiatedCost in the walkthrough - it is stripped from
// find_top_supplier_overdue_orders results for non-admin actors.
// This lets the Supply Chain E2E workload exercise all three cases
// described in the Foundgine walkthrough (docs-site/walkthrough/index.html):
// calculated evidence -> execution, candidates but no assurance -> ask, and
// no candidates at all.
var suppliers = new[]
{
    ("Acme Industrial", "TX", 482000m, 391000m),
    ("Globex Components", "TX", 210000m, 175000m),
    ("Northstar Supply", "CA", 300000m, 240000m),
    ("Southline Parts", "CA", 300000m, 250000m)
};
foreach (var s in suppliers)
    await Exec(db, tx,
        "INSERT INTO suppliers(supplier_name,email,state,total_order_value,negotiated_cost) VALUES(@n,@e,@st,@v,@nc);",
        ("n", s.Item1), ("e", s.Item1.Replace(" ", ".").ToLowerInvariant() + "@example.test"), ("st", s.Item2),
        ("v", s.Item3), ("nc", s.Item4));
foreach (var c in new[]
         {
             ("Electronics", "Sensors and control equipment"), ("Hardware", "Industrial hardware"),
             ("Packaging", "Packaging materials")
         })
    await Exec(db, tx, "INSERT INTO categories(category_name,description) VALUES(@n,@d);", ("n", c.Item1),
        ("d", c.Item2));
foreach (var w in new[]
         {
             ("Auckland Warehouse", "Auckland, NZ", 12000), ("Hamilton Warehouse", "Hamilton, NZ", 9000),
             ("Wellington Warehouse", "Wellington, NZ", 11000)
         })
    await Exec(db, tx, "INSERT INTO warehouses(warehouse_name,location,capacity_m3) VALUES(@n,@l,@c);", ("n", w.Item1),
        ("l", w.Item2), ("c", w.Item3));
foreach (var c in new[] { "NZ Freight", "Pacific Logistics" })
    await Exec(db, tx, "INSERT INTO carriers(carrier_name,tracking_url_template) VALUES(@n,@u);", ("n", c),
        ("u", "https://tracking.example.test/{tracking}"));

for (var i = 1; i <= 12; i++)
{
    var supplierId = (i - 1) % 3 + 1;
    var categoryId = (i - 1) % 3 + 1;
    await Exec(db, tx,
        "INSERT INTO products(product_name,sku,category_id,supplier_id,unit_price,weight_kg) VALUES(@n,@s,@c,@sup,@p,@w);",
        ("n", $"Product {i:00}"), ("s", $"SKU-{1000 + i}"), ("c", categoryId), ("sup", supplierId),
        ("p", 5m + i * 7.25m), ("w", 0.5m + i / 10m));
}

for (var warehouse = 1; warehouse <= 3; warehouse++)
for (var product = 1; product <= 12; product++)
    await Exec(db, tx,
        "INSERT INTO inventory(warehouse_id,product_id,quantity_on_hand,reorder_level) VALUES(@w,@p,@q,@r);",
        ("w", warehouse), ("p", product), ("q", (warehouse + product) % 5 == 0 ? 4 : 50 + warehouse * 10 + product),
        ("r", 10));

for (var i = 1; i <= customers; i++)
    await Exec(db, tx,
        "INSERT INTO customers(first_name,last_name,email,phone,shipping_address) VALUES(@f,@l,@e,@p,@a);",
        ("f", $"Customer{i}"), ("l", "Demo"), ("e", $"customer{i}@example.test"), ("p", $"021000{i:000}"),
        ("a", $"{i} Demo Street, NZ"));

// Purchase orders for the top-supplier walkthrough case (state=TX resolves
// unambiguously to Acme, supplier_id=1): two overdue and one received.
await Exec(db, tx,
    "INSERT INTO purchase_orders(supplier_id,expected_date,received_date,status) VALUES(1,CURRENT_DATE-19,NULL,'Open');");
await Exec(db, tx,
    "INSERT INTO purchase_orders(supplier_id,expected_date,received_date,status) VALUES(1,CURRENT_DATE-11,NULL,'Open');");
await Exec(db, tx,
    "INSERT INTO purchase_orders(supplier_id,expected_date,received_date,status) VALUES(1,CURRENT_DATE-30,CURRENT_DATE-28,'Received');");
// A purchase order for the runner-up in TX (Globex) confirms ranking, not
// just presence, decides which supplier's orders come back.
await Exec(db, tx,
    "INSERT INTO purchase_orders(supplier_id,expected_date,received_date,status) VALUES(2,CURRENT_DATE-5,NULL,'Open');");

await tx.CommitAsync();
Console.WriteLine(
    $"Seeded supply chain graph: {customers} customers, 12 products, 4 suppliers, 3 warehouses, 36 inventory rows, 4 purchase orders.");

static async Task Exec(NpgsqlConnection db, NpgsqlTransaction tx, string sql, params (string, object)[] ps)
{
    await using var c = new NpgsqlCommand(sql, db, tx);
    foreach (var p in ps) c.Parameters.AddWithValue(p.Item1, p.Item2);
    await c.ExecuteNonQueryAsync();
}

static int GetInt(string n, int d) => int.TryParse(Environment.GetEnvironmentVariable(n), out var v) ? v : d;