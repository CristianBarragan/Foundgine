package com.foundgine.samples.comparison.spring;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;

/** JPA rows for the legacy schema. Attribute names are what Filters addresses; column names are the legacy ones. */
public final class Entities {
    private Entities() {}

    @Entity @Table(name = "tbl_customer")
    public static class CustomerRow {
        @Id @Column(name = "cust_id") Integer id;
        @Column(name = "first_nm") String firstName;
        @Column(name = "last_nm") String lastName;
        @Column(name = "email_addr") String email;
    }

    @Entity @Table(name = "tbl_product")
    public static class ProductRow {
        @Id @Column(name = "prod_id") Integer id;
        @Column(name = "prod_sku") String sku;
        @Column(name = "prod_title") String name;
        @Column(name = "list_price") BigDecimal price;
    }

    @Entity @Table(name = "tbl_order")
    public static class OrderRow {
        @Id @Column(name = "ord_id") Integer id;
        @Column(name = "cust_id") Integer customerId;
        @Column(name = "ord_status") String status;
        @Column(name = "ord_total") BigDecimal total;
    }

    @Entity @Table(name = "tbl_order_line")
    public static class OrderLineRow {
        @Id @Column(name = "line_id") Integer id;
        @Column(name = "ord_id") Integer orderId;
        @Column(name = "prod_id") Integer productId;
        @Column(name = "qty") Integer quantity;
        @Column(name = "unit_price") BigDecimal unitPrice;
    }
}
