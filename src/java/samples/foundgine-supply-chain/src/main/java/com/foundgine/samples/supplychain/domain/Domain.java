package com.foundgine.samples.supplychain.domain;

import java.math.BigDecimal;

/**
 * Annotation-free domain for the three entities the starter exposes. Semantic names, aliases, dimensions,
 * storage and entity maps are declared in {@code com.foundgine.samples.supplychain.model}; the JPA classes
 * in {@code jpa/} are only the persistence shape.
 */
public final class Domain {
    private Domain() {}

    public record Supplier(int id, String name, String country, BigDecimal riskScore, String tenantId) {}

    public record Warehouse(int id, int businessUnitId, String name, String tenantId) {}

    public record Product(int id, String sku, String name, String category, BigDecimal safetyStock) {}
}
