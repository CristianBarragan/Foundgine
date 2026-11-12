package com.foundgine.providers.storage.orm.jpa;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

@Entity
@Table(name = "jpa_baseline_customer")
public class JpaBaselineCustomer {
    @Id
    private Long id;

    @Column(nullable = false)
    private String name;

    @Column(nullable = true)
    private String region;

    @Column(nullable = false)
    private Boolean enabled;

    protected JpaBaselineCustomer() {}

    public JpaBaselineCustomer(Long id, String name, String region) {
        this.id = id;
        this.name = name;
        this.region = region;
        this.enabled = false;
    }

    public Long getId() { return id; }
    public String getName() { return name; }
    public String getRegion() { return region; }
    public Boolean getEnabled() { return enabled; }
}
