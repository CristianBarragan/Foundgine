package com.foundgine.benchmark;
import jakarta.persistence.*; import java.math.BigDecimal;
@Entity @Table(name="suppliers") public class Supplier { @Id @Column(name="supplier_id") public Integer id; @Column(name="supplier_name") public String name; @Column(name="country") public String country; @Column(name="risk_score") public BigDecimal riskScore; @Column(name="tenant_id") public String tenantId; protected Supplier(){} }
