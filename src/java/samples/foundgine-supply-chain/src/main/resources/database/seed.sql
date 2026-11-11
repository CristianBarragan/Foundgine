INSERT INTO suppliers VALUES (1,'Acme Components','US',12.50,'tenant-a'),(2,'Borealis Metals','DE',31.00,'tenant-a'),(3,'Other Tenant Ltd','US',5.00,'tenant-b') ON CONFLICT DO NOTHING;
INSERT INTO warehouses VALUES (1,10,'Austin DC','tenant-a'),(2,10,'Rotterdam DC','tenant-a'),(3,20,'Hidden DC','tenant-b') ON CONFLICT DO NOTHING;
INSERT INTO products VALUES (1,'SKU-100','Bearing','hardware',50),(2,'SKU-200','Gasket','hardware',120) ON CONFLICT DO NOTHING;
