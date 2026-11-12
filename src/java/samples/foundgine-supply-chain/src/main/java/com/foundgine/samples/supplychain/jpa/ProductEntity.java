package com.foundgine.samples.supplychain.jpa;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

import java.math.BigDecimal;

/** JPA entity behind the semantic {@code Product}. Attribute names equal the semantic field names. */
@Entity
@Table(name = "products")
public class ProductEntity {
    @Id @Column(name = "product_id") private Integer id;
    @Column(name = "sku") private String sku;
    @Column(name = "product_name") private String name;
    @Column(name = "category") private String category;
    @Column(name = "safety_stock") private BigDecimal safetyStock;

    protected ProductEntity() {}

    public ProductEntity(Integer id, String sku, String name, String category, BigDecimal safetyStock) {
        this.id = id; this.sku = sku; this.name = name; this.category = category; this.safetyStock = safetyStock;
    }

    public Integer getId() { return id; }
    public void setId(Integer id) { this.id = id; }
    public String getSku() { return sku; }
    public void setSku(String sku) { this.sku = sku; }
    public String getName() { return name; }
    public void setName(String name) { this.name = name; }
    public String getCategory() { return category; }
    public void setCategory(String category) { this.category = category; }
    public BigDecimal getSafetyStock() { return safetyStock; }
    public void setSafetyStock(BigDecimal safetyStock) { this.safetyStock = safetyStock; }
}
