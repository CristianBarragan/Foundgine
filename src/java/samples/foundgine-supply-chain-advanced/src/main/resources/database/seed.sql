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

-- Seeded so that ranking "top supplier" by state produces two different
-- shapes of evidence for the ambiguity-resolution demo capability:
--   - state=TX has a clear, unambiguous top supplier (Acme, 482000 > 210000)
--   - state=CA has a TIE at the top (Northstar and Southline both 300000)
--   - any other state (e.g. NY) has zero suppliers, for the not-found case
-- negotiated_cost is a commercially sensitive field: it is stripped from
-- find_top_supplier_overdue_orders results for non-admin actors.
INSERT INTO suppliers(supplier_name,state,total_order_value,negotiated_cost) VALUES
 ('Acme Industrial','TX',482000,391000),
 ('Globex Components','TX',210000,175000),
 ('Northstar Supply','CA',300000,240000),
 ('Southline Parts','CA',300000,250000) ON CONFLICT (supplier_name) DO NOTHING;

-- Two overdue and one received purchase order for Acme (the unambiguous TX
-- winner), plus one overdue for Globex (the TX runner-up) so that ranking,
-- not just presence, is what decides which supplier's orders come back.
INSERT INTO purchase_orders(supplier_id,expected_date,received_date,status)
 SELECT s.supplier_id, CURRENT_DATE - 19, NULL, 'Open' FROM suppliers s WHERE s.supplier_name = 'Acme Industrial'
 AND NOT EXISTS (SELECT 1 FROM purchase_orders po WHERE po.supplier_id = s.supplier_id AND po.expected_date = CURRENT_DATE - 19);
INSERT INTO purchase_orders(supplier_id,expected_date,received_date,status)
 SELECT s.supplier_id, CURRENT_DATE - 11, NULL, 'Open' FROM suppliers s WHERE s.supplier_name = 'Acme Industrial'
 AND NOT EXISTS (SELECT 1 FROM purchase_orders po WHERE po.supplier_id = s.supplier_id AND po.expected_date = CURRENT_DATE - 11);
INSERT INTO purchase_orders(supplier_id,expected_date,received_date,status)
 SELECT s.supplier_id, CURRENT_DATE - 30, CURRENT_DATE - 28, 'Received' FROM suppliers s WHERE s.supplier_name = 'Acme Industrial'
 AND NOT EXISTS (SELECT 1 FROM purchase_orders po WHERE po.supplier_id = s.supplier_id AND po.expected_date = CURRENT_DATE - 30);
INSERT INTO purchase_orders(supplier_id,expected_date,received_date,status)
 SELECT s.supplier_id, CURRENT_DATE - 5, NULL, 'Open' FROM suppliers s WHERE s.supplier_name = 'Globex Components'
 AND NOT EXISTS (SELECT 1 FROM purchase_orders po WHERE po.supplier_id = s.supplier_id AND po.expected_date = CURRENT_DATE - 5);
