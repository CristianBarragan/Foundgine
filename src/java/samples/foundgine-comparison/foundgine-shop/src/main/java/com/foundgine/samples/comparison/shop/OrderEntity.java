package com.foundgine.samples.comparison.shop;

import com.foundgine.providers.aot.FoundgineOrmEntity; // AOT discovery marker; semantic/persistence configuration lives in ShopModel.
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;

/** JPA entity behind the semantic Order. Attribute names equal the semantic field names. */
@FoundgineOrmEntity
@Entity
@Table(name = "tbl_order")
public class OrderEntity {
    @Id @Column(name = "ord_id") private Integer id;
    @Column(name = "cust_id") private Integer customerId;
    @Column(name = "ord_status") private String status;
    @Column(name = "ord_total") private BigDecimal total;

    public OrderEntity() {}

    public Integer getId() { return id; }
    public Integer getCustomerId() { return customerId; }
    public String getStatus() { return status; }
    public BigDecimal getTotal() { return total; }
}
