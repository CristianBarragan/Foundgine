package com.foundgine.samples.comparison.shop;

import com.foundgine.core.semantic.configuration.*;
import com.foundgine.samples.comparison.shop.Domain.*;

/** The whole Foundgine declaration: semantics, storage, mapping, relationships. build() validates it at startup. */
public final class ShopModel extends FoundgineModel {
    @Override
    public void configure(FoundgineModelRegistrar model) {
        model.configure(CustomerSemantics.class);
        model.configure(OrderSemantics.class);
        model.configure(OrderLineSemantics.class);
        model.configure(ProductSemantics.class);
        model.configure(CustomerTable.class);
        model.configure(ProductTable.class);
        model.configure(OrderTable.class);
        model.configure(OrderLineTable.class);
        model.configure(Maps.CustomerMap.class);
        model.configure(Maps.ProductMap.class);
        model.configure(Maps.OrderMap.class);
        model.configure(Maps.OrderLineMap.class);
        model.configure(Connections.class);
    }

    // Semantic: names and relationships between models. Customer.orders also defines Order.customer.
    static final class CustomerSemantics extends FoundgineModelConfiguration<Customer> {
        @Override public void configure(FoundgineModelBuilder<Customer> m) {
            m.toEntity("Customer"); m.hasFieldNaming(FieldNaming.PASCAL_CASE);
            m.hasMany("orders", Order.class).withOne("customer");
        }
    }

    static final class OrderSemantics extends FoundgineModelConfiguration<Order> {
        @Override public void configure(FoundgineModelBuilder<Order> m) {
            m.toEntity("Order"); m.hasFieldNaming(FieldNaming.PASCAL_CASE);
            m.hasMany("lines", OrderLine.class);
        }
    }

    static final class OrderLineSemantics extends FoundgineModelConfiguration<OrderLine> {
        @Override public void configure(FoundgineModelBuilder<OrderLine> m) {
            m.toEntity("OrderLine"); m.hasFieldNaming(FieldNaming.PASCAL_CASE);
            m.hasOne("product", Product.class);
        }
    }

    static final class ProductSemantics extends FoundgineModelConfiguration<Product> {
        @Override public void configure(FoundgineModelBuilder<Product> m) {
            m.toEntity("Product"); m.hasFieldNaming(FieldNaming.PASCAL_CASE);
        }
    }

    // Persistence: table, key, legacy column names.
    static final class CustomerTable extends FoundginePersistenceConfiguration<Customer> {
        @Override public void configure(FoundginePersistenceBuilder<Customer> m) {
            m.toTable("tbl_customer"); m.hasKey(Customer::id);
            m.property(Customer::id).hasColumnName("cust_id");
            m.property(Customer::firstName).hasColumnName("first_nm");
            m.property(Customer::lastName).hasColumnName("last_nm");
            m.property(Customer::email).hasColumnName("email_addr");
        }
    }

    static final class ProductTable extends FoundginePersistenceConfiguration<Product> {
        @Override public void configure(FoundginePersistenceBuilder<Product> m) {
            m.toTable("tbl_product"); m.hasKey(Product::id);
            m.property(Product::id).hasColumnName("prod_id");
            m.property(Product::sku).hasColumnName("prod_sku");
            m.property(Product::name).hasColumnName("prod_title");
            m.property(Product::price).hasColumnName("list_price");
        }
    }

    static final class OrderTable extends FoundginePersistenceConfiguration<Order> {
        @Override public void configure(FoundginePersistenceBuilder<Order> m) {
            m.toTable("tbl_order"); m.hasKey(Order::id);
            m.property(Order::id).hasColumnName("ord_id");
            m.property(Order::customerId).hasColumnName("cust_id");
            m.property(Order::status).hasColumnName("ord_status");
            m.property(Order::total).hasColumnName("ord_total");
        }
    }

    static final class OrderLineTable extends FoundginePersistenceConfiguration<OrderLine> {
        @Override public void configure(FoundginePersistenceBuilder<OrderLine> m) {
            m.toTable("tbl_order_line"); m.hasKey(OrderLine::id);
            m.property(OrderLine::id).hasColumnName("line_id");
            m.property(OrderLine::orderId).hasColumnName("ord_id");
            m.property(OrderLine::productId).hasColumnName("prod_id");
            m.property(OrderLine::quantity).hasColumnName("qty");
            m.property(OrderLine::unitPrice).hasColumnName("unit_price");
        }
    }

    // Model member -> storage member (the domain types double as both; the JPA classes are the persistence shape).
    static final class Maps {
        private Maps() {}

        static final class CustomerMap extends FoundgineEntityMap<Customer, Customer> {
            @Override public void configure(FoundgineEntityMapBuilder<Customer, Customer> m) {
                m.property(Customer::id).to(Customer::id); m.property(Customer::firstName).to(Customer::firstName);
                m.property(Customer::lastName).to(Customer::lastName); m.property(Customer::email).to(Customer::email);
            }
        }

        static final class ProductMap extends FoundgineEntityMap<Product, Product> {
            @Override public void configure(FoundgineEntityMapBuilder<Product, Product> m) {
                m.property(Product::id).to(Product::id); m.property(Product::sku).to(Product::sku);
                m.property(Product::name).to(Product::name); m.property(Product::price).to(Product::price);
            }
        }

        static final class OrderMap extends FoundgineEntityMap<Order, Order> {
            @Override public void configure(FoundgineEntityMapBuilder<Order, Order> m) {
                m.property(Order::id).to(Order::id); m.property(Order::customerId).to(Order::customerId);
                m.property(Order::status).to(Order::status); m.property(Order::total).to(Order::total);
            }
        }

        static final class OrderLineMap extends FoundgineEntityMap<OrderLine, OrderLine> {
            @Override public void configure(FoundgineEntityMapBuilder<OrderLine, OrderLine> m) {
                m.property(OrderLine::id).to(OrderLine::id); m.property(OrderLine::orderId).to(OrderLine::orderId);
                m.property(OrderLine::productId).to(OrderLine::productId); m.property(OrderLine::quantity).to(OrderLine::quantity);
                m.property(OrderLine::unitPrice).to(OrderLine::unitPrice);
            }
        }
    }

    // Relationship -> foreign key. One connection serves both directions of a relationship.
    static final class Connections extends FoundgineConnectionConfiguration {
        @Override public void configure(FoundgineConnectionBuilder c) {
            c.hasRelationship(Customer.class, Order.class).using(Customer.class, Order.class)
                    .hasForeignKey(Order::customerId).references(Customer::id);
            c.hasRelationship(Order.class, OrderLine.class).using(Order.class, OrderLine.class)
                    .hasForeignKey(OrderLine::orderId).references(Order::id);
            c.hasRelationship(OrderLine.class, Product.class).using(OrderLine.class, Product.class)
                    .hasForeignKeyOnSource(OrderLine::productId).references(Product::id);
        }
    }
}
