package com.foundgine.benchmark;
import jakarta.persistence.*; import java.math.BigDecimal;
@Entity @Table(name="products") public class Product { @Id @Column(name="product_id") public Integer id; @Column(name="sku") public String sku; @Column(name="product_name") public String name; @Column(name="category") public String category; @Column(name="safety_stock") public BigDecimal safetyStock; protected Product(){} }
