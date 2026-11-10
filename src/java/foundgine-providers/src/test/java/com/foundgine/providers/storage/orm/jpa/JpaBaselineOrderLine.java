package com.foundgine.providers.storage.orm.jpa;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

@Entity
@Table(name = "jpa_baseline_order_line")
public class JpaBaselineOrderLine {
    @Id private Long id;
    @Column(nullable = false) private Long customerId;
    @Column(nullable = false) private Long tenantId;
    @Column(nullable = false) private String status;

    protected JpaBaselineOrderLine() {}

    public JpaBaselineOrderLine(Long id, Long customerId, Long tenantId, String status) {
        this.id = id;
        this.customerId = customerId;
        this.tenantId = tenantId;
        this.status = status;
    }
}
