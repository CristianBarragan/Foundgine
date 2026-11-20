package com.foundgine.samples.comparison.shop;

import java.math.BigDecimal;

/** API models: what callers see. The relationships between them are declared in ShopModel, not as members. */
public final class Domain {
    private Domain() {}

    public record Customer(int id, String firstName, String lastName, String email) {}
    public record Product(int id, String sku, String name, BigDecimal price) {}
    public record Order(int id, int customerId, String status, BigDecimal total) {}
    public record OrderLine(int id, int orderId, int productId, int quantity, BigDecimal unitPrice) {}
}
