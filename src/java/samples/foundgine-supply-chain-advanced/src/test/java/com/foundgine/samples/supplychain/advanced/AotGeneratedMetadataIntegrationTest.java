package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.samples.supplychain.advanced.generated.GeneratedFoundgineMetadata;
import org.junit.jupiter.api.Test;

import java.util.stream.StreamSupport;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Proves the Advanced Supply Chain sample actually consumes the compile-time
 * generated metadata artifact instead of merely configuring the processor.
 */
class AotGeneratedMetadataIntegrationTest {
    @Test
    void generatedRegistryContainsAnnotatedSupplyChainEntities() {
        var registry = GeneratedFoundgineMetadata.build();
        var entities = StreamSupport.stream(registry.entities().spliterator(), false)
                .map(EntityMetadata::name)
                .toList();

        assertTrue(entities.contains("Company"));
        assertTrue(entities.contains("Supplier"));
        assertTrue(entities.contains("PurchaseOrder"));
        assertTrue(entities.contains("InventoryMovement"));
        assertTrue(entities.contains("ComplianceIncident"));
        assertFalse(entities.isEmpty());
    }

    @Test
    void generatedMetadataPreservesAliasesAndEventMetadata() {
        var registry = GeneratedFoundgineMetadata.build();
        var supplier = StreamSupport.stream(registry.entities().spliterator(), false)
                .filter(e -> e.name().equals("Supplier"))
                .findFirst()
                .orElseThrow();

        assertTrue(supplier.aliases().stream()
                .anyMatch(a -> a.name().equals("Vendor") && a.weight() == 95));

        var movement = StreamSupport.stream(registry.entities().spliterator(), false)
                .filter(e -> e.name().equals("InventoryMovement"))
                .findFirst()
                .orElseThrow();
        assertTrue(movement.event());
        assertNotNull(movement.temporalColumn());
    }
}
