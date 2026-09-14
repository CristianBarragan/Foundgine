package com.foundgine.runtime;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.mutation.*;
import com.foundgine.core.semantic.security.execution.SecurityResourceLimits;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/** Mirrors the C# mutation resource-limit security tests. */
class MutationSecurityResourceLimitParityTest {
	private static final EntityId ENTITY = new EntityId(1);

	@Test
	void rejectsExcessiveMutationOperationCount() {
		var operations = List.of(operation(), operation(), operation());
		var request = new SemanticMutationRequest(new SemanticMutationOperationGraph(operations));
		var d = SecurityResourceLimits.defaults();
		var limits = copy(d, 2, d.maxMutationFieldsPerOperation(), d.maxMutationReturnFieldsPerOperation(),
				d.maxMutationDependencies(), d.maxMutationEffects());
		assertThrows(IllegalStateException.class,
				() -> MutationSecurityResourceLimitValidator.validate(request, limits));
	}

	@Test
	void rejectsExcessiveFieldsPerMutation() {
		var fields = List.of(new SemanticMutationField(new FieldId(1), "a"),
				new SemanticMutationField(new FieldId(2), "b"), new SemanticMutationField(new FieldId(3), "c"));
		var request = new SemanticMutationRequest(
				new SemanticMutationOperationGraph(List.of(new SemanticMutationOperation(ENTITY,
						SemanticMutationKind.CREATE, fields, null, List.of(), List.of(), List.of(), List.of()))));
		var d = SecurityResourceLimits.defaults();
		var limits = copy(d, d.maxMutationOperations(), 2, d.maxMutationReturnFieldsPerOperation(),
				d.maxMutationDependencies(), d.maxMutationEffects());
		assertThrows(IllegalStateException.class,
				() -> MutationSecurityResourceLimitValidator.validate(request, limits));
	}

	@Test
	void rejectsExcessiveDependencies() {
		var dependencies = List.of(new SemanticMutationDependency(0, 1, new FieldId(101), new FieldId(201)),
				new SemanticMutationDependency(0, 1, new FieldId(102), new FieldId(202)),
				new SemanticMutationDependency(0, 1, new FieldId(103), new FieldId(203)));
		var first = operation();
		var second = new SemanticMutationOperation(ENTITY, SemanticMutationKind.UPDATE, List.of(), null, List.of(),
				List.of(), List.of(), dependencies);
		var request = new SemanticMutationRequest(new SemanticMutationOperationGraph(List.of(first, second)));
		var d = SecurityResourceLimits.defaults();
		var limits = copy(d, d.maxMutationOperations(), d.maxMutationFieldsPerOperation(),
				d.maxMutationReturnFieldsPerOperation(), 2, d.maxMutationEffects());
		assertThrows(IllegalStateException.class,
				() -> MutationSecurityResourceLimitValidator.validate(request, limits));
	}

	@Test
	void rejectsExcessiveReturnFieldsPerMutation() {
		var returns = List.of(new FieldId(1), new FieldId(2), new FieldId(3));
		var request = new SemanticMutationRequest(
				new SemanticMutationOperationGraph(List.of(new SemanticMutationOperation(ENTITY,
						SemanticMutationKind.CREATE, List.of(), null, List.of(), returns, List.of(), List.of()))));
		var d = SecurityResourceLimits.defaults();
		var limits = copy(d, d.maxMutationOperations(), d.maxMutationFieldsPerOperation(), 2,
				d.maxMutationDependencies(), d.maxMutationEffects());
		assertThrows(IllegalStateException.class,
				() -> MutationSecurityResourceLimitValidator.validate(request, limits));
	}

	private static SemanticMutationOperation operation() {
		return new SemanticMutationOperation(ENTITY, SemanticMutationKind.CREATE,
				List.of(new SemanticMutationField(new FieldId(1), "x")), null, List.of(), List.of(), List.of(),
				List.of());
	}

	private static SecurityResourceLimits copy(SecurityResourceLimits d, int ops, int fields, int returns, int deps,
			int effects) {
		return new SecurityResourceLimits(d.maxSelectionDepth(), d.maxOperationGraphNodes(), d.maxOperationGraphDepth(),
				d.maxOperationGraphEdges(), d.maxOperationGraphFields(), d.maxSelectionNodes(), d.maxFilterDepth(),
				d.maxFilterNodes(), d.maxOrderTerms(), d.maxOrderPathDepth(), d.maxPageSize(), d.maxOffset(),
				d.maxCursorLength(), ops, fields, returns, deps, effects);
	}
}
