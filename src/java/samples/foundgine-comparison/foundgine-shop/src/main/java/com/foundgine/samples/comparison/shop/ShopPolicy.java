package com.foundgine.samples.comparison.shop;

import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.extensions.graphql.http.GraphQLHttpHandler;
import java.util.HashMap;

/**
 * Row-level rule: a customer only sees their own Customer row and their own Orders. One predicate, applied by the
 * planner at every level of every query (roots and nested), whatever protocol carried the request.
 */
final class ShopPolicy implements ISemanticAuthorizationPolicy {
    private final EntityId customer;
    private final EntityId order;

    ShopPolicy(MetadataRegistry metadata) {
        this.customer = find(metadata, "Customer");
        this.order = find(metadata, "Order");
    }

    @Override public boolean canAccessEntity(EntityId id) { return true; }
    @Override public boolean canAccessField(EntityId entity, FieldId field) { return true; }
    @Override public boolean canAccessRelationship(EntityId source, RelationshipId relationship) { return true; }
    // Writes are denied by the interface defaults: this API is read-only.

    @Override
    public AuthorizationPredicate getPredicate(EntityId entity, AuthorizationOperation operation) {
        var user = GraphQLHttpHandler.currentPrincipal();
        if (operation != AuthorizationOperation.READ || user == null || !user.isInRole("customer")) return null;
        String member;
        if (entity.equals(customer)) member = "id";
        else if (entity.equals(order)) member = "customerId";
        else return null;
        var row = AuthorizationPredicate.member(AuthorizationPredicate.resourceParameter("resource"), member);
        // Fail closed when the claim is missing.
        if (customerId(user) == null) return AuthorizationPredicate.equal(row, AuthorizationPredicate.constant("-1"));
        return AuthorizationPredicate.equal(row,
                AuthorizationPredicate.member(AuthorizationPredicate.contextParameter("user"), "customerId"));
    }

    /** What the rule may reference at execution time: a validated claim, never GraphQL input. Shared by every protocol. */
    static ExecutionContext context() {
        var values = new HashMap<String, Object>();
        var user = GraphQLHttpHandler.currentPrincipal();
        if (user != null && user.isInRole("customer") && customerId(user) != null) values.put("user.customerId", customerId(user));
        return new ExecutionContext(values);
    }

    static Integer customerId(GraphQLHttpHandler.Principal user) {
        try { return Integer.valueOf(user.claim("customer_id")); } catch (RuntimeException missingOrMalformed) { return null; }
    }

    private static EntityId find(MetadataRegistry metadata, String name) {
        for (var e : metadata.entities()) if (e.name().equals(name)) return e.entityId();
        throw new IllegalStateException("Entity '" + name + "' is not in the metadata.");
    }
}
