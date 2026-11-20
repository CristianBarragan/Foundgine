package com.foundgine.samples.comparison.spring;

import java.math.BigDecimal;

/** GraphQL/API models: what callers see. Different names and shape from the JPA rows. */
public final class Models {
    private Models() {}

    public record Customer(int id, String firstName, String lastName, String email) {}
    public record Product(int id, String sku, String name, BigDecimal price) {}
    public record Order(int id, int customerId, String status, BigDecimal total) {}
    public record OrderLine(int id, int orderId, int productId, int quantity, BigDecimal unitPrice) {}
}
