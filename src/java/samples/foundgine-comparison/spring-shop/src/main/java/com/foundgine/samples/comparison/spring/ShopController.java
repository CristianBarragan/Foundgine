package com.foundgine.samples.comparison.spring;

import com.foundgine.samples.comparison.spring.Entities.*;
import graphql.schema.DataFetchingEnvironment;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.graphql.data.method.annotation.BatchMapping;
import org.springframework.graphql.data.method.annotation.QueryMapping;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.stereotype.Controller;

@Controller
public class ShopController {
    private final ShopData data;

    public ShopController(ShopData data) { this.data = data; }

    /** Row-level rule: a customer only ever sees rows of their own customer_id claim; null means unrestricted. */
    private static Integer scope() {
        var auth = SecurityContextHolder.getContext().getAuthentication();
        if (auth.getAuthorities().stream().noneMatch(a -> a.getAuthority().equals("ROLE_customer"))) return null;
        try {
            return Integer.valueOf(((Jwt) auth.getPrincipal()).getClaimAsString("customer_id"));
        } catch (RuntimeException missingOrMalformed) {
            return -1; // fail closed: matches no row
        }
    }

    @QueryMapping("customer") @PreAuthorize("hasAnyRole('manager','customer')")
    public List<Models.Customer> customers(DataFetchingEnvironment env) {
        return data.<CustomerRow>find(Filters.CUSTOMER, env.getArgument("where"), env.getArgument("order"), "id", scope())
                .stream().map(Mappings::customer).toList();
    }

    @QueryMapping("order") @PreAuthorize("hasAnyRole('manager','customer')")
    public List<Models.Order> orders(DataFetchingEnvironment env) {
        return data.<OrderRow>find(Filters.ORDER, env.getArgument("where"), env.getArgument("order"), "customerId", scope())
                .stream().map(Mappings::order).toList();
    }

    @QueryMapping("product") @PreAuthorize("hasAnyRole('manager','customer')")
    public List<Models.Product> products(DataFetchingEnvironment env) {
        return data.<ProductRow>find(Filters.PRODUCT, env.getArgument("where"), env.getArgument("order"), null, null)
                .stream().map(Mappings::product).toList();
    }

    @QueryMapping("orderLine") @PreAuthorize("hasRole('manager')")
    public List<Models.OrderLine> orderLines(DataFetchingEnvironment env) {
        return data.<OrderLineRow>find(Filters.LINE, env.getArgument("where"), env.getArgument("order"), null, null)
                .stream().map(Mappings::line).toList();
    }

    // Nested fields: one batched query per field. Each hangs off an already-scoped root, so there is no second check.
    @BatchMapping
    public Map<Models.Customer, List<Models.Order>> orders(List<Models.Customer> customers) {
        var byCustomer = data.findIn(OrderRow.class, "customerId", ids(customers, Models.Customer::id)).stream()
                .map(Mappings::order).collect(Collectors.groupingBy(Models.Order::customerId));
        return group(customers, c -> byCustomer.getOrDefault(c.id(), List.of()));
    }

    @BatchMapping
    public Map<Models.Order, Models.Customer> customer(List<Models.Order> orders) {
        var byId = data.findIn(CustomerRow.class, "id", ids(orders, Models.Order::customerId)).stream()
                .map(Mappings::customer).collect(Collectors.toMap(Models.Customer::id, Function.identity()));
        var out = new HashMap<Models.Order, Models.Customer>();
        for (var o : orders) out.put(o, byId.get(o.customerId()));
        return out;
    }

    @BatchMapping
    public Map<Models.Order, List<Models.OrderLine>> lines(List<Models.Order> orders) {
        var byOrder = data.findIn(OrderLineRow.class, "orderId", ids(orders, Models.Order::id)).stream()
                .map(Mappings::line).collect(Collectors.groupingBy(Models.OrderLine::orderId));
        return group(orders, o -> byOrder.getOrDefault(o.id(), List.of()));
    }

    @BatchMapping
    public Map<Models.OrderLine, Models.Product> product(List<Models.OrderLine> lines) {
        var byId = data.findIn(ProductRow.class, "id", ids(lines, Models.OrderLine::productId)).stream()
                .map(Mappings::product).collect(Collectors.toMap(Models.Product::id, Function.identity()));
        var out = new HashMap<Models.OrderLine, Models.Product>();
        for (var l : lines) out.put(l, byId.get(l.productId()));
        return out;
    }

    private static <T> Collection<Integer> ids(List<T> items, Function<T, Integer> key) {
        return items.stream().map(key).distinct().toList();
    }

    private static <K, V> Map<K, V> group(List<K> keys, Function<K, V> value) {
        var out = new HashMap<K, V>();
        for (var k : keys) out.put(k, value.apply(k));
        return out;
    }
}
