package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.RelationshipCardinality;
import com.foundgine.core.semantic.SemanticModelBuilder;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Parity port of C# SemanticLexiconProjectionTests. */
class SemanticLexiconProjectionParityTest {

	@Test
	void buildRejectsNullContract() {
		assertThrows(NullPointerException.class, () -> SemanticLexiconProjection.build(null));
	}

	@Test
	void buildProjectsEntityAsEntityAndNodeWithAliases() {
		var customer = new EntityId(1);
		var contract = new SemanticModelBuilder()
				.entity(customer, "Customer", e -> e.alias("Client").identity(new FieldId(1), "Id")).build().freeze()
				.createSnapshot();

		var entries = SemanticLexiconProjection.build(contract);
		var entityEntry = single(entries,
				x -> x.kind() == SemanticLexicalCandidateKind.ENTITY && x.canonicalName().equals("Customer"));
		assertEquals(customer, entityEntry.entityId());
		assertTrue(entityEntry.effectiveAliases().contains("Client"));
		assertEquals("Customer", entityEntry.searchText());

		var nodeEntry = single(entries,
				x -> x.kind() == SemanticLexicalCandidateKind.NODE && x.canonicalName().equals("Customer"));
		assertEquals(customer, nodeEntry.entityId());
		assertTrue(nodeEntry.effectiveAliases().contains("Client"));
	}

	@Test
	void buildProjectsFieldWithOwningEntityNameAndAliases() {
		var customer = new EntityId(1);
		var nameField = new FieldId(2);
		var contract = new SemanticModelBuilder()
				.entity(customer, "Customer", e -> e.identity(new FieldId(1), "Id")
						.field(nameField, "Name", String.class).fieldAlias(nameField, "Full Name"))
				.build().freeze().createSnapshot();

		var fieldEntry = single(SemanticLexiconProjection.build(contract),
				x -> x.kind() == SemanticLexicalCandidateKind.FIELD);
		assertEquals("Name", fieldEntry.canonicalName());
		assertEquals(customer, fieldEntry.entityId());
		assertEquals(nameField, fieldEntry.fieldId());
		assertEquals("Customer Name", fieldEntry.searchText());
		assertTrue(fieldEntry.effectiveAliases().contains("Full Name"));
	}

	@Test
	void buildProjectsRelationshipWithSourceTargetAndAliases() {
		var customer = new EntityId(1);
		var order = new EntityId(2);
		var relationshipId = RelationshipId.create("Customer", "Orders");
		var contract = new SemanticModelBuilder().entity(order, "Order", e -> e.identity(new FieldId(2), "Id"))
				.entity(customer, "Customer",
						e -> e.identity(new FieldId(1), "Id")
								.relationship(relationshipId, "Orders", order, RelationshipCardinality.MANY)
								.relationshipAlias(relationshipId, "Purchases"))
				.build().freeze().createSnapshot();

		var relationshipEntry = single(SemanticLexiconProjection.build(contract),
				x -> x.kind() == SemanticLexicalCandidateKind.RELATIONSHIP);
		assertEquals("Orders", relationshipEntry.canonicalName());
		assertEquals(customer, relationshipEntry.sourceEntityId());
		assertEquals(order, relationshipEntry.targetEntityId());
		assertEquals("Customer Orders Order", relationshipEntry.searchText());
		assertTrue(relationshipEntry.effectiveAliases().contains("Purchases"));
	}

	@Test
	void buildProjectsLogicalTraversalWithEndpointNames() {
		var customer = new EntityId(1);
		var order = new EntityId(2);
		var line = new EntityId(3);
		var contract = new SemanticModelBuilder()
				.entity(customer, "Customer",
						e -> e.identity(new FieldId(1), "Id").relationship(new RelationshipId(1), "Orders", order,
								RelationshipCardinality.MANY))
				.entity(order, "Order",
						e -> e.identity(new FieldId(2), "Id").relationship(new RelationshipId(2), "Lines", line,
								RelationshipCardinality.MANY))
				.entity(line, "OrderLine", e -> e.identity(new FieldId(3), "Id"))
				.traversal("Customer", "PurchasedLines", "Orders", "Lines").build().freeze().createSnapshot();

		var traversalEntry = single(SemanticLexiconProjection.build(contract),
				x -> x.kind() == SemanticLexicalCandidateKind.TRAVERSAL);
		assertEquals("PurchasedLines", traversalEntry.canonicalName());
		assertEquals(customer, traversalEntry.sourceEntityId());
		assertEquals(line, traversalEntry.targetEntityId());
		assertEquals("Customer PurchasedLines OrderLine", traversalEntry.searchText());
		assertNull(traversalEntry.entityId());
	}

	@Test
	void bareEntityProducesOnlyEntityAndNodeEntries() {
		var contract = new SemanticModelBuilder()
				.entity(new EntityId(1), "Standalone", e -> e.identity(new FieldId(1), "Id")).build().freeze()
				.createSnapshot();

		var entries = SemanticLexiconProjection.build(contract);
		assertEquals(2, entries.size());
		assertTrue(entries.stream().anyMatch(x -> x.kind() == SemanticLexicalCandidateKind.ENTITY));
		assertTrue(entries.stream().anyMatch(x -> x.kind() == SemanticLexicalCandidateKind.NODE));
	}

	private static SemanticLexiconEntry single(List<SemanticLexiconEntry> entries,
			java.util.function.Predicate<SemanticLexiconEntry> predicate) {
		var matches = entries.stream().filter(predicate).toList();
		assertEquals(1, matches.size());
		return matches.getFirst();
	}
}
