package com.foundgine.providers.aot;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Port of C# {@code Foundgine.Providers.Aot.Tests.IdentityDeterminismTests}.
 *
 * <p>
 * The C# suite drives a real Roslyn {@code CSharpGeneratorDriver} against
 * hand-written source text and inspects the emitted {@code GeneratedMetadata}
 * class. Java has no compile-time source-generator equivalent here —
 * {@link FoundgineMetadataGenerator} is a documented "runtime reflection
 * fallback" (see its class javadoc) that computes the same identities from
 * annotated {@code Class<?>} objects at runtime instead of from parsed source
 * text. The properties under test — that identity depends on declared
 * semantic names, not on declaration order or on what else is in the module —
 * are still meaningful and portable, so this file exercises them against the
 * reflection path.
 *
 * <p>
 * Two C# cases are intentionally NOT ported here, because they don't have a
 * behavior to assert on the Java side — see the notes at the bottom of this
 * file rather than silently dropping them.
 */
class IdentityDeterminismParityTest {

	// ---- fixtures for "declaration order independence" -------------------

	@FoundgineEntity(name = "Customer")
	static class CustomerFieldsInDeclaredOrder {
		@FoundgineField(name = "Id")
		public int id;
		@FoundgineField(name = "Name")
		public String name;
		@FoundgineField(name = "Status")
		public String status;
	}

	@FoundgineEntity(name = "Customer")
	static class CustomerFieldsReordered {
		@FoundgineField(name = "Status")
		public String status;
		@FoundgineField(name = "Name")
		public String name;
		@FoundgineField(name = "Id")
		public int id;
	}

	@Test
	void generatedEntityAndFieldIdsAreIndependentOfDeclarationOrder() {
		var inOrder = byFieldName(FoundgineMetadataGenerator.fields(CustomerFieldsInDeclaredOrder.class));
		var reordered = byFieldName(FoundgineMetadataGenerator.fields(CustomerFieldsReordered.class));

		assertEquals(inOrder.get("Id").entity(), reordered.get("Id").entity());
		assertEquals(inOrder.get("Id").id(), reordered.get("Id").id());
		assertEquals(inOrder.get("Name").id(), reordered.get("Name").id());
		assertEquals(inOrder.get("Status").id(), reordered.get("Status").id());
	}

	// ---- fixtures for "unrelated module addition" -------------------------

	@FoundgineEntity(name = "Customer")
	static class CustomerAlone {
		@FoundgineField(name = "Id")
		public int id;
		@FoundgineField(name = "Name")
		public String name;
	}

	@FoundgineEntity(name = "Order")
	static class UnrelatedOrder {
		@FoundgineField(name = "Id")
		public int id;
		@FoundgineField(name = "Number")
		public String number;
	}

	@Test
	void existingGeneratedIdsSurviveAddingAnUnrelatedType() {
		var independent = byFieldName(FoundgineMetadataGenerator.fields(CustomerAlone.class));
		var customerEntity = independent.get("Id").entity();
		var composed = byFieldName(FoundgineMetadataGenerator.fields(CustomerAlone.class, UnrelatedOrder.class).stream()
				.filter(field -> field.entity().equals(customerEntity))
				.toList());

		assertEquals(independent.get("Id").entity(), composed.get("Id").entity());
		assertEquals(independent.get("Id").id(), composed.get("Id").id());
		assertEquals(independent.get("Name").id(), composed.get("Name").id());
	}

	// ---- fixtures for "aliases don't change identity" ---------------------

	@FoundgineEntity(name = "Customer")
	static class CustomerNoAlias {
		@FoundgineField(name = "Name")
		public String name;
	}

	@FoundgineEntity(name = "Customer")
	@FoundgineAlias("Client")
	static class CustomerWithAlias {
		@FoundgineField(name = "Name")
		@FoundgineAlias("DisplayName")
		public String name;
	}

	@Test
	void changingAliasesDoesNotChangeGeneratedIdentity() {
		var canonical = byFieldName(FoundgineMetadataGenerator.fields(CustomerNoAlias.class));
		var aliased = byFieldName(FoundgineMetadataGenerator.fields(CustomerWithAlias.class));

		assertEquals(canonical.get("Name").entity(), aliased.get("Name").entity());
		assertEquals(canonical.get("Name").id(), aliased.get("Name").id());
	}

	private static Map<String, GeneratedSemanticField> byFieldName(List<GeneratedSemanticField> fields) {
		return fields.stream().collect(Collectors.toMap(GeneratedSemanticField::name, f -> f));
	}

	/*
	 * NOT PORTED — Generated_ids_are_identical_across_independent_compilations:
	 * this asserts that two separate Roslyn compilations (different assembly
	 * names) of the same source produce the same generated ids. There is no
	 * Java analog to "separate compilation" for a runtime reflection call —
	 * calling FoundgineMetadataGenerator.fields() twice on the same Class<?>
	 * is not a comparable scenario, so there's nothing distinct to assert
	 * here that generatedEntityAndFieldIdsAreIndependentOfDeclarationOrder
	 * doesn't already cover.
	 *
	 * NOT PORTED — Explicit_zero_ids_are_rejected_by_the_generator and
	 * Duplicate_explicit_entity_ids_are_rejected: these are real gaps, not
	 * test gaps. FoundgineEntity.id() and FoundgineField.id() both default to
	 * 0, so FoundgineMetadataGenerator currently cannot distinguish "id
	 * explicitly set to 0" from "id not set" (the ternary in fields() is
	 * `entity.id() != 0 ? validateExplicitId(...) : hash(...)`), and it
	 * performs no cross-type duplicate-id check at all when given multiple
	 * classes. GeneratorSemanticIdentity.validateExplicitId(0, ...) does
	 * throw in isolation (see GeneratorSemanticIdentityParityTest), but nothing
	 * in FoundgineMetadataGenerator's actual entity/field id resolution can
	 * reach that call with an explicit 0, and nothing rejects two types
	 * sharing an id. Writing a test that pretends otherwise would be
	 * misleading — this should go back as a product gap, not a test gap.
	 */
}
