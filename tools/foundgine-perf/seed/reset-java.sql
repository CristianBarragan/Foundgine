-- Benchmark data for the Java group: the sample's own demo rows (src/main/resources/db/demo/R__demo_data.sql), applied by the
-- HARNESS so the baseline does not depend on Foundgine's Flyway demo location.
TRUNCATE suppliers, warehouses, products;
INSERT INTO suppliers VALUES (1,'Acme Components','US',12.50,'tenant-a'),(2,'Borealis Metals','DE',31.00,'tenant-a'),(3,'Other Tenant Ltd','US',5.00,'tenant-b');
INSERT INTO warehouses VALUES (1,10,'Austin DC','tenant-a'),(2,10,'Rotterdam DC','tenant-a'),(3,20,'Hidden DC','tenant-b');
INSERT INTO products VALUES (1,'SKU-100','Bearing','hardware',50),(2,'SKU-200','Gasket','hardware',120);
