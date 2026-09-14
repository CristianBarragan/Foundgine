package com.foundgine.core.semantic;

import static org.junit.jupiter.api.Assertions.assertEquals;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.resolution.SemanticRequestResolver;

import org.junit.jupiter.api.Test;

import java.util.List;

/**
 * Parity port of Foundgine.Semantics.Tests.SemanticContractRuntimeBoundaryTests.
 *
 * <p>The runtime resolver is intentionally constructed from the immutable {@link
 * SemanticContractSnapshot}; provider metadata is not part of this boundary. The Java API has no C#
 * model-constructor compatibility overload, so the snapshot-only contract is the canonical surface.
 */
class SemanticContractRuntimeBoundaryParityTest {

    @Test
    void resolverAcceptsTheRuntimeContractSnapshot() {
        var entityId = EntityId.create("Customer");
        var fieldId = FieldId.create("Customer", "Id");

        var model =
                new SemanticModelBuilder()
                        .entity(
                                entityId,
                                "Customer",
                                entity ->
                                        entity.field(fieldId, "Id", int.class)
                                                .identity(fieldId, "Id"))
                        .build()
                        .freeze();

        var contract = new SemanticContractSnapshot(model);
        var request =
                new SemanticRequest(
                        entityId,
                        List.of(new SemanticSelection(fieldId, null, List.of())),
                        null,
                        null);

        var graph = new SemanticRequestResolver(contract).resolve(request);

        assertEquals(1, graph.nodes().size());
        assertEquals(contract.contractFingerprint(), model.contractFingerprint());
    }

    @Test
    void resolverIsProviderIndependentAtTheContractBoundary() {
        var entityId = new EntityId(1);
        var fieldId = new FieldId(2);

        var model =
                new SemanticModelBuilder()
                        .entity(
                                entityId,
                                "Customer",
                                entity ->
                                        entity.identity(fieldId, "Id")
                                                .field(new FieldId(3), "Name", String.class))
                        .build()
                        .freeze();

        var contract = new SemanticContractSnapshot(model);
        var graph =
                new SemanticRequestResolver(contract)
                        .resolve(
                                new SemanticRequest(
                                        entityId,
                                        List.of(new SemanticSelection(fieldId, null, List.of())),
                                        null,
                                        null));

        assertEquals(entityId, graph.nodes().get(0).entityId());
        assertEquals(List.of(fieldId), graph.nodes().get(0).fields());
    }
}
