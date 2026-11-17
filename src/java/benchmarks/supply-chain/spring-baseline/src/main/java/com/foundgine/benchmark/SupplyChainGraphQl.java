package com.foundgine.benchmark;

import jakarta.persistence.EntityManager;
import jakarta.persistence.Query;
import jakarta.transaction.Transactional;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.graphql.data.method.annotation.Argument;
import org.springframework.graphql.data.method.annotation.MutationMapping;
import org.springframework.graphql.data.method.annotation.QueryMapping;
import org.springframework.security.core.Authentication;
import org.springframework.stereotype.Controller;

@Controller
public class SupplyChainGraphQl {
    private final EntityManager em;

    public SupplyChainGraphQl(EntityManager em) {
        this.em = em;
    }

    @QueryMapping
    public List<Map<String, Object>> product(@Argument Integer first, Authentication authentication) {
        Caller caller = Caller.from(authentication);
        return rows(em.createNativeQuery(
                "select product_id,sku,product_name,category,safety_stock from products order by product_id limit :n")
            .setParameter("n", limit(first)))
            .stream()
            .map(row -> map(
                "id", row[0],
                "sku", row[1],
                "name", row[2],
                "category", row[3],
                "safetyStock", row[4]))
            .toList();
    }

    @QueryMapping
    public List<Map<String, Object>> supplier(@Argument Integer first, Authentication authentication) {
        Caller caller = Caller.from(authentication);
        if (caller.role().equals("CUSTOMER")) {
            throw new SecurityException("supplier read role required");
        }

        return rows(em.createNativeQuery(
                "select supplier_id,supplier_name,country,risk_score,tenant_id "
                    + "from suppliers where tenant_id=:tenant order by supplier_id limit :n")
            .setParameter("tenant", caller.tenantId())
            .setParameter("n", limit(first)))
            .stream()
            .map(row -> {
                Map<String, Object> result = map(
                    "id", row[0],
                    "name", row[1],
                    "country", row[2],
                    "tenantId", row[4]);
                if (caller.canSeeRisk()) {
                    result.put("riskScore", row[3]);
                }
                return result;
            })
            .toList();
    }

    @QueryMapping
    public List<Map<String, Object>> warehouse(@Argument Integer first, Authentication authentication) {
        Caller caller = Caller.from(authentication);
        if (caller.role().equals("CUSTOMER")) {
            throw new SecurityException("warehouse read role required");
        }

        return rows(em.createNativeQuery(
                "select warehouse_id,business_unit_id,warehouse_name,tenant_id "
                    + "from warehouses where tenant_id=:tenant order by warehouse_id limit :n")
            .setParameter("tenant", caller.tenantId())
            .setParameter("n", limit(first)))
            .stream()
            .map(row -> map(
                "id", row[0],
                "businessUnitId", row[1],
                "name", row[2],
                "tenantId", row[3]))
            .toList();
    }

    @MutationMapping
    @Transactional
    public Map<String, Object> updateWarehouse(
            @Argument Map<String, Object> input,
            @Argument Map<String, Object> where,
            Authentication authentication) {
        Caller caller = Caller.from(authentication);
        if (!caller.canWrite()) {
            throw new SecurityException("warehouse write role required");
        }

        int id = ((Number) where.get("id")).intValue();
        String name = (String) input.get("name");
        int updated = em.createNativeQuery(
                "update warehouses set warehouse_name=:name "
                    + "where warehouse_id=:id and tenant_id=:tenant")
            .setParameter("name", name)
            .setParameter("id", id)
            .setParameter("tenant", caller.tenantId())
            .executeUpdate();
        if (updated == 0) {
            return null;
        }

        return rows(em.createNativeQuery(
                "select warehouse_id,business_unit_id,warehouse_name,tenant_id "
                    + "from warehouses where warehouse_id=:id and tenant_id=:tenant")
            .setParameter("id", id)
            .setParameter("tenant", caller.tenantId()))
            .stream()
            .findFirst()
            .map(row -> map(
                "id", row[0],
                "businessUnitId", row[1],
                "name", row[2],
                "tenantId", row[3]))
            .orElse(null);
    }

    @SuppressWarnings("unchecked")
    private static List<Object[]> rows(Query query) {
        return (List<Object[]>) query.getResultList();
    }

    private static int limit(Integer requested) {
        return Math.max(1, Math.min(requested == null ? 50 : requested, 500));
    }

    private static Map<String, Object> map(Object... values) {
        Map<String, Object> result = new LinkedHashMap<>();
        for (int index = 0; index < values.length; index += 2) {
            result.put((String) values[index], values[index + 1]);
        }
        return result;
    }
}
