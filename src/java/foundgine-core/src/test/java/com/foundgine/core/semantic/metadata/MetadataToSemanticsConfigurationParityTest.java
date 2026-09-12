package com.foundgine.core.semantic.metadata;

import com.foundgine.core.abstractions.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Port of Foundgine.Semantics.Tests.MetadataToSemanticsConfigurationTests. */
class MetadataToSemanticsConfigurationParityTest {
	@Test
	void discoveredMetadataCanBeEnrichedWithoutGeneratedIdentityReferences() {
		var metadata = new MetadataRegistry();
		metadata.register(entity(1, "Customer", 1));
		metadata.register(entity(2, "CustomerRelationship", 2, 3));
		metadata.register(entity(3, "Contract", 4));
		metadata.register(entity(4, "Transaction", 5, 6));
		metadata.register(new RelationshipMetadata(new RelationshipId(10), new EntityId(1), new EntityId(2),
				"relationships", ref(1, 1), ref(2, 3)));
		metadata.register(new RelationshipMetadata(new RelationshipId(11), new EntityId(2), new EntityId(3), "contract",
				ref(2, 2), ref(3, 4), false, null));
		metadata.register(new RelationshipMetadata(new RelationshipId(12), new EntityId(3), new EntityId(4),
				"transactions", ref(3, 4), ref(4, 6)));
		var model = SemanticModelDiscovery.fromMetadata(metadata)
				.traversal("Customer", "transactions", "relationships", "contract", "transactions").build();
		var traversal = model.getTraversal(new EntityId(1), "transactions");
		assertEquals(new EntityId(4), traversal.target());
		assertEquals(java.util.List.of(new RelationshipId(10), new RelationshipId(11), new RelationshipId(12)),
				traversal.path());
	}

	private static ColumnReference ref(int e, int c) {
		return new ColumnReference(new EntityId(e), new ColumnId(c));
	}

	private static EntityMetadata entity(int id, String name, int... fields) {
		var list = new java.util.ArrayList<FieldMetadata>();
		for (int f : fields)
			list.add(new FieldMetadata(new FieldId(f),
					f == fields[0] ? "Id" : (f == 3 ? "CustomerId" : f == 6 ? "ContractId" : "Field" + f),
					Integer.class, ref(id, f)));
		return new EntityMetadata(new EntityId(id), name, java.util.List.of(), null, list, ref(id, fields[0]), null,
				false, null, null);
	}
}
