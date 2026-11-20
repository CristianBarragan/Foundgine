package com.foundgine.samples.comparison.shop;

import com.foundgine.providers.aot.FoundgineOrmEntity; // AOT discovery marker; semantic/persistence configuration lives in ShopModel.
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;

/** JPA entity behind the semantic Product. Attribute names equal the semantic field names. */
@FoundgineOrmEntity
@Entity
@Table(name = "tbl_product")
public class ProductEntity {
    @Id @Column(name = "prod_id") private Integer id;
    @Column(name = "prod_sku") private String sku;
    @Column(name = "prod_title") private String name;
    @Column(name = "list_price") private BigDecimal price;

    public ProductEntity() {}

    public Integer getId() { return id; }
    public String getSku() { return sku; }
    public String getName() { return name; }
    public BigDecimal getPrice() { return price; }
}
