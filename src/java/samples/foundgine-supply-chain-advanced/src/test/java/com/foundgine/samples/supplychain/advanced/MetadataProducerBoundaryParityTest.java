package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.semantic.metadata.IMetadataCatalog;
import com.foundgine.samples.supplychain.advanced.generated.GeneratedFoundgineMetadata;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** Content-level port of the C# MetadataProducerBoundaryTests. */
class MetadataProducerBoundaryParityTest {
    @Test
    void generatedMetadataExposesStructuralCatalogBoundary() {
        IMetadataCatalog catalog = GeneratedFoundgineMetadata.build();
        var entities = catalog.entities().spliterator();
        var entityList = java.util.stream.StreamSupport.stream(entities, false).toList();
        var relationships = java.util.stream.StreamSupport.stream(catalog.relationships().spliterator(), false).toList();
        assertEquals(17, entityList.size());
        assertEquals(15, relationships.size());
        assertTrue(entityList.stream().anyMatch(e -> e.name().equals("Product")));
        assertTrue(entityList.stream().anyMatch(e -> e.name().equals("ComplianceIncident")));
        for (var relationship : relationships) {
            var source = catalog.getEntity(relationship.source());
            var target = catalog.getEntity(relationship.target());
            assertTrue(source.columns().stream().anyMatch(c -> c.id().equals(relationship.sourceKey().columnId())));
            assertTrue(target.columns().stream().anyMatch(c -> c.id().equals(relationship.targetKey().columnId())));
        }
    }

    @Test
    void semanticConfigurationConsumesGeneratedMetadataTopology() {
        var catalog = GeneratedFoundgineMetadata.build();
        var model = SupplyChainSemanticModel.MODEL;
        var entityCount = java.util.stream.StreamSupport.stream(catalog.entities().spliterator(), false).count();
        var relationshipCount = java.util.stream.StreamSupport.stream(catalog.relationships().spliterator(), false).count();
        assertEquals(entityCount, model.entities().size());
        assertEquals(relationshipCount,
                model.entities().stream().flatMap(e -> e.relationships().stream()).count());
    }
}
