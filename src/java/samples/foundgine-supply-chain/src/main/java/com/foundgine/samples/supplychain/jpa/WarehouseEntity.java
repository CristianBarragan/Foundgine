package com.foundgine.samples.supplychain.jpa;

import com.foundgine.providers.aot.FoundgineOrmEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** JPA entity behind the semantic {@code Warehouse}. Attribute names equal the semantic field names. */
@FoundgineOrmEntity
@Entity
@Table(name = "warehouses")
public class WarehouseEntity {
    @Id @Column(name = "warehouse_id") private Integer id;
    @Column(name = "business_unit_id") private Integer businessUnitId;
    @Column(name = "warehouse_name") private String name;
    @Column(name = "tenant_id") private String tenantId;

    public WarehouseEntity() {}

    public WarehouseEntity(Integer id, Integer businessUnitId, String name, String tenantId) {
        this.id = id; this.businessUnitId = businessUnitId; this.name = name; this.tenantId = tenantId;
    }

    public Integer getId() { return id; }
    public void setId(Integer id) { this.id = id; }
    public Integer getBusinessUnitId() { return businessUnitId; }
    public void setBusinessUnitId(Integer businessUnitId) { this.businessUnitId = businessUnitId; }
    public String getName() { return name; }
    public void setName(String name) { this.name = name; }
    public String getTenantId() { return tenantId; }
    public void setTenantId(String tenantId) { this.tenantId = tenantId; }
}
