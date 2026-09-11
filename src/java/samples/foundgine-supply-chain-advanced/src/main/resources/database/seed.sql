INSERT INTO customers(customer_id,customer_name,tenant_id) VALUES
 (1,'Alice','tenant-a'),(2,'Bob','tenant-a') ON CONFLICT DO NOTHING;
INSERT INTO products(product_id,sku,product_name,category,safety_stock,unit_price) VALUES
 (1,'MOTOR-X','Industrial Motor','Motors',30,12.25),
 (2,'CTRL-X','Motor Controller','Controls',40,19.50),
 (3,'PCB-X','Controller PCB','Electronics',80,26.75),
 (4,'CAP-X','High Reliability Capacitor','Electronics',120,34.00),
 (5,'RES-X','Precision Resistor','Electronics',100,41.25),
 (6,'PACK-X','Customer Assembly','Finished Goods',25,48.50) ON CONFLICT DO NOTHING;
INSERT INTO warehouses(warehouse_id,warehouse_name,business_unit_id,tenant_id) VALUES
 (1,'Auckland DC',1,'tenant-a'),(2,'Hamilton DC',1,'tenant-a'),(3,'Restricted Rotorua DC',2,'tenant-b') ON CONFLICT DO NOTHING;
INSERT INTO inventory_lots(lot_id,warehouse_id,product_id,on_hand,reserved,quarantined,received_on) VALUES
 (900,1,4,70,20,10,'2026-08-01'),(901,1,5,90,75,0,'2026-08-02'),
 (902,1,3,25,10,5,'2026-08-04'),(903,3,4,5000,0,0,'2026-08-01') ON CONFLICT DO NOTHING;
