package com.foundgine.samples.comparison.spring;

import com.foundgine.samples.comparison.spring.Entities.*;

/** Hand-written row -> model mapping, one method per model (the analogue of Mappings.cs in the Hot Chocolate sample). */
final class Mappings {
    private Mappings() {}

    static Models.Customer customer(CustomerRow r) { return new Models.Customer(r.id, r.firstName, r.lastName, r.email); }
    static Models.Product product(ProductRow r) { return new Models.Product(r.id, r.sku, r.name, r.price); }
    static Models.Order order(OrderRow r) { return new Models.Order(r.id, r.customerId, r.status, r.total); }
    static Models.OrderLine line(OrderLineRow r) {
        return new Models.OrderLine(r.id, r.orderId, r.productId, r.quantity, r.unitPrice);
    }
}
