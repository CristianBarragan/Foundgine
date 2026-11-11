package com.foundgine.samples.supplychain.advanced.jpa;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

import java.math.BigDecimal;
import java.time.LocalDate;

/**
 * JPA entity behind the semantic {@code InventoryLot}. {@code warehouseId} is a plain column: the
 * Warehouse -> InventoryLot relationship is declared once, in the fluent model's connections, and Foundgine
 * (not a JPA association) joins it. Columns follow database/schema.sql.
 */
@Entity
@Table(name = "inventory_lots")
public class InventoryLotEntity {
    @Id @Column(name = "lot_id") private Integer id;
    @Column(name = "warehouse_id") private Integer warehouseId;
    @Column(name = "product_id") private Integer productId;
    @Column(name = "on_hand") private BigDecimal onHand;
    @Column(name = "reserved") private BigDecimal reserved;
    @Column(name = "quarantined") private BigDecimal quarantined;
    @Column(name = "received_on") private LocalDate receivedOn;

    protected InventoryLotEntity() {}

    public InventoryLotEntity(Integer id, Integer warehouseId, Integer productId, BigDecimal onHand,
                              BigDecimal reserved, BigDecimal quarantined, LocalDate receivedOn) {
        this.id = id; this.warehouseId = warehouseId; this.productId = productId; this.onHand = onHand;
        this.reserved = reserved; this.quarantined = quarantined; this.receivedOn = receivedOn;
    }

    public Integer getId() { return id; }
    public Integer getWarehouseId() { return warehouseId; }
    public Integer getProductId() { return productId; }
    public BigDecimal getOnHand() { return onHand; }
    public BigDecimal getReserved() { return reserved; }
    public BigDecimal getQuarantined() { return quarantined; }
    public LocalDate getReceivedOn() { return receivedOn; }
}
