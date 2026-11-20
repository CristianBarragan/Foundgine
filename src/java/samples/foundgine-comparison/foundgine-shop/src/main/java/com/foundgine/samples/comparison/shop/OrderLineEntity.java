package com.foundgine.samples.comparison.shop;

import com.foundgine.providers.aot.FoundgineOrmEntity; // AOT discovery marker; semantic/persistence configuration lives in ShopModel.
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;

/** JPA entity behind the semantic OrderLine. Attribute names equal the semantic field names. */
@FoundgineOrmEntity
@Entity
@Table(name = "tbl_order_line")
public class OrderLineEntity {
    @Id @Column(name = "line_id") private Integer id;
    @Column(name = "ord_id") private Integer orderId;
    @Column(name = "prod_id") private Integer productId;
    @Column(name = "qty") private Integer quantity;
    @Column(name = "unit_price") private BigDecimal unitPrice;

    public OrderLineEntity() {}

    public Integer getId() { return id; }
    public Integer getOrderId() { return orderId; }
    public Integer getProductId() { return productId; }
    public Integer getQuantity() { return quantity; }
    public BigDecimal getUnitPrice() { return unitPrice; }
}
