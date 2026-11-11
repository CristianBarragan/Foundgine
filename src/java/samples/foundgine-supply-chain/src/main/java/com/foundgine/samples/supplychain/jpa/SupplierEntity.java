package com.foundgine.samples.supplychain.jpa;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

import java.math.BigDecimal;

/** JPA entity behind the semantic {@code Supplier}. Attribute names equal the semantic field names. */
@Entity
@Table(name = "suppliers")
public class SupplierEntity {
    @Id @Column(name = "supplier_id") private Integer id;
    @Column(name = "supplier_name") private String name;
    @Column(name = "country") private String country;
    @Column(name = "risk_score") private BigDecimal riskScore;
    @Column(name = "tenant_id") private String tenantId;

    protected SupplierEntity() {}

    public SupplierEntity(Integer id, String name, String country, BigDecimal riskScore, String tenantId) {
        this.id = id; this.name = name; this.country = country; this.riskScore = riskScore; this.tenantId = tenantId;
    }

    public Integer getId() { return id; }
    public void setId(Integer id) { this.id = id; }
    public String getName() { return name; }
    public void setName(String name) { this.name = name; }
    public String getCountry() { return country; }
    public void setCountry(String country) { this.country = country; }
    public BigDecimal getRiskScore() { return riskScore; }
    public void setRiskScore(BigDecimal riskScore) { this.riskScore = riskScore; }
    public String getTenantId() { return tenantId; }
    public void setTenantId(String tenantId) { this.tenantId = tenantId; }
}
