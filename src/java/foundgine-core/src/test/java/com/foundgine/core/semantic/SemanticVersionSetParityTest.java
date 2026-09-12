package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;

/**
 * Port of {@code Foundgine.Core.Semantic.Tests.SemanticVersioningTests}
 * (declared alongside {@code SemanticCapabilityContractTests} in the C#
 * source file).
 */
class SemanticVersionSetParityTest {

	@Test
	void versionSetIsStableForEquivalentModels() {
		var first = SemanticVersionSet.forModel(buildModel(false));
		var second = SemanticVersionSet.forModel(buildModel(false));

		assertEquals(first.semanticModelVersion(), second.semanticModelVersion());
		assertEquals(1, first.capabilityContractVersion());
		assertEquals(1, first.capabilityVersion());
		assertEquals(1, first.intentVersion());
		assertEquals(1, first.planVersion());
	}

	@Test
	void semanticModelVersionChangesWhenTopologyChanges() {
		var first = SemanticVersionSet.forModel(buildModel(false));
		var changed = SemanticVersionSet.forModel(buildModel(true));

		assertNotEquals(first.semanticModelVersion(), changed.semanticModelVersion());
	}

	private static SemanticModel buildModel(boolean includeExtraEntity) {
		var builder = new SemanticModelBuilder().entity(new EntityId(1), "Customer",
				e -> e.identity(new FieldId(1), "Id").field(new FieldId(1), "Name", String.class));

		if (includeExtraEntity) {
			builder.entity(new EntityId(2), "Account",
					e -> e.identity(new FieldId(2), "Id").field(new FieldId(2), "Number", String.class));
		}

		return builder.build();
	}
}
