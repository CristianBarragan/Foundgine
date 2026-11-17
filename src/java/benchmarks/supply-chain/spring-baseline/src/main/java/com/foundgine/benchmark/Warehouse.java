package com.foundgine.benchmark;
import jakarta.persistence.*;
@Entity @Table(name="warehouses") public class Warehouse { @Id @Column(name="warehouse_id") public Integer id; @Column(name="business_unit_id") public Integer businessUnitId; @Column(name="warehouse_name") public String name; @Column(name="tenant_id") public String tenantId; protected Warehouse(){} }
