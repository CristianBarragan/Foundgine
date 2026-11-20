-- Shared legacy-style schema. Column names deliberately differ from the API model member names.
CREATE TABLE tbl_customer (
  cust_id    serial PRIMARY KEY,
  first_nm   varchar(100) NOT NULL,
  last_nm    varchar(100) NOT NULL,
  email_addr varchar(320) NOT NULL UNIQUE
);
CREATE TABLE tbl_product (
  prod_id    serial PRIMARY KEY,
  prod_sku   varchar(64) NOT NULL UNIQUE,
  prod_title varchar(200) NOT NULL,
  list_price numeric(12,2) NOT NULL CHECK (list_price >= 0)
);
CREATE TABLE tbl_order (
  ord_id     serial PRIMARY KEY,
  cust_id    int NOT NULL REFERENCES tbl_customer(cust_id),
  ord_status varchar(20) NOT NULL CHECK (ord_status IN ('PENDING','SHIPPED','DELIVERED')),
  ord_total  numeric(14,2) NOT NULL
);
CREATE INDEX ix_order_cust ON tbl_order(cust_id);
CREATE TABLE tbl_order_line (
  line_id    serial PRIMARY KEY,
  ord_id     int NOT NULL REFERENCES tbl_order(ord_id),
  prod_id    int NOT NULL REFERENCES tbl_product(prod_id),
  qty        int NOT NULL CHECK (qty > 0),
  unit_price numeric(12,2) NOT NULL
);
CREATE INDEX ix_line_ord ON tbl_order_line(ord_id);

INSERT INTO tbl_customer(first_nm,last_nm,email_addr) VALUES
 ('Ada','Lovelace','ada@example.com'),('Grace','Hopper','grace@example.com'),('Alan','Turing','alan@example.com');
INSERT INTO tbl_product(prod_sku,prod_title,list_price) VALUES
 ('SKU-100','Widget',10.00),('SKU-200','Gadget',25.50),('SKU-300','Gizmo',99.99),('SKU-400','Doohickey',5.25),('SKU-500','Thingamajig',150.00);
INSERT INTO tbl_order(cust_id,ord_status,ord_total) VALUES
 (1,'PENDING',45.50),(1,'SHIPPED',199.98),(1,'DELIVERED',10.00),
 (2,'PENDING',150.00),(2,'SHIPPED',130.50),
 (3,'DELIVERED',31.25),(3,'PENDING',99.99),(3,'SHIPPED',300.00);
INSERT INTO tbl_order_line(ord_id,prod_id,qty,unit_price) VALUES
 (1,1,2,10.00),(1,2,1,25.50),
 (2,3,2,99.99),
 (3,1,1,10.00),
 (4,5,1,150.00),
 (5,3,1,99.99),(5,2,1,25.50),(5,4,1,5.25),
 (6,4,5,5.25),(6,1,1,10.00)
 ,(7,3,1,99.99),
 (8,5,2,150.00);
