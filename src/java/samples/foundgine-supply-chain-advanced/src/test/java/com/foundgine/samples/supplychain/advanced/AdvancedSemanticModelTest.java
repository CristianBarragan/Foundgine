package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.authorization.*;
import com.foundgine.core.semantic.resolution.*;
import com.foundgine.samples.supplychain.advanced.authorization.*;
import com.foundgine.samples.supplychain.advanced.semantics.*;
import java.util.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class AdvancedSemanticModelTest {
    @Test void semanticContractContainsAdvancedSupplyChainTopology() {
        var model = SupplyChainSemanticModel.MODEL;
        assertEquals("Product", model.resolveEntity("Item").name());
        assertEquals("ProductComponent", model.resolveEntity("ProductComponent").name());
        assertTrue(model.get(SupplyChainSemanticModel.PRODUCT).relationships().stream()
                .anyMatch(r -> r.name().equals("components")));
        assertTrue(model.get(SupplyChainSemanticModel.SUPPLIER).relationships().stream()
                .anyMatch(r -> r.name().equals("certifications")));
    }

    @Test void productAliasesAndConstraintsArePartOfSemanticContract() {
        var product = SupplyChainSemanticModel.MODEL.get(SupplyChainSemanticModel.PRODUCT);
        assertTrue(product.effectiveAliases().stream().anyMatch(a -> a.name().equalsIgnoreCase("Item")));
        var sku = product.fields().stream().filter(f -> f.name().equals("Sku")).findFirst().orElseThrow();
        assertTrue(sku.effectiveAliases().stream().anyMatch(a -> a.name().equalsIgnoreCase("PartNumber")));
        assertTrue(sku.effectiveConstraints().stream().anyMatch(c -> c.kind() == SemanticConstraintKind.PATTERN));
    }

    @Test void warehouseClaimNarrowsSemanticAuthorizationPredicate() {
        var policy = SupplyChainAuthorization.create("tenant-a", SupplyChainAuthorization.Role.ANALYST,
                Map.of("warehouse", "2"));
        var predicate = policy.getPredicate(SupplyChainSemanticModel.INVENTORY_LOT, AuthorizationOperation.READ);
        assertNotNull(predicate);
        assertEquals(AuthorizationPredicateKind.AND, predicate.kind());
    }

    @Test void sensitiveFieldsRemainRoleBound() {
        var analyst = SupplyChainAuthorization.create("tenant-a", SupplyChainAuthorization.Role.ANALYST, Map.of());
        var customer = SupplyChainAuthorization.create("tenant-a", SupplyChainAuthorization.Role.CUSTOMER, Map.of());
        assertTrue(analyst.canAccessField(SupplyChainSemanticModel.SUPPLIER, SupplyChainAuthorization.field("Supplier", "RiskScore")));
        assertFalse(customer.canAccessField(SupplyChainSemanticModel.SUPPLIER, SupplyChainAuthorization.field("Supplier", "RiskScore")));
        assertFalse(analyst.canAccessField(SupplyChainSemanticModel.INVENTORY_LOT, SupplyChainAuthorization.field("InventoryLot", "Quarantined")));
    }

    @Test void lexicalAliasCanCommitToProduct() {
        var source = (ISemanticLexicalCandidateSource) request -> {
            String token = request.token();
            if (token.equalsIgnoreCase("item"))
                return List.of(new SemanticLexicalCandidate(token, SemanticLexicalCandidateKind.ENTITY, "Product", .99,
                        SupplyChainSemanticModel.PRODUCT, null, null, null, SupplyChainSemanticModel.PRODUCT, null, List.of()));
            return List.of();
        };
        var resolver = new SemanticLexicalResolver(SupplyChainSemanticModel.MODEL.createSnapshot(), source);
        var decision = resolver.ground("item");
        assertEquals(GroundingOutcome.COMMITTED, decision.outcome());
        assertEquals(SupplyChainSemanticModel.PRODUCT, decision.committed().rootEntity());
    }
}
