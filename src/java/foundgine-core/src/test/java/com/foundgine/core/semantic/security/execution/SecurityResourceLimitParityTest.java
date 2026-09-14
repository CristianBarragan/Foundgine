package com.foundgine.core.semantic.security.execution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.SemanticSelection;
import com.foundgine.core.semantic.query.*;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Protocol-neutral resource-limit parity: limits apply before planning
 * regardless of transport.
 */
class SecurityResourceLimitParityTest {
	private static final EntityId CUSTOMER = new EntityId(1);
	private static final FieldId ID = new FieldId(1);
	private static final RelationshipId ORDERS = new RelationshipId(2);

	@Test
	void nonJsonCallersCannotBypassSelectionDepth() {
		var request = new SemanticRequest(CUSTOMER,
				List.of(new SemanticSelection(ID, null, List.of(new SemanticSelection(ID, null, List.of())))), null,
				null);
		var d = SecurityResourceLimits.defaults();
		var limits = limits(1, d.maxOperationGraphNodes(), d.maxOperationGraphDepth(), d.maxOperationGraphEdges(),
				d.maxOperationGraphFields(), d.maxSelectionNodes(), d.maxFilterDepth(), d.maxFilterNodes(),
				d.maxOrderTerms(), d.maxOrderPathDepth(), d.maxPageSize(), d.maxOffset(), d.maxCursorLength(),
				d.maxMutationOperations(), d.maxMutationFieldsPerOperation(), d.maxMutationReturnFieldsPerOperation(),
				d.maxMutationDependencies(), d.maxMutationEffects());
		var ex = assertThrows(IllegalStateException.class,
				() -> SecurityResourceLimitValidator.validate(request, limits));
		assertTrue(ex.getMessage().toLowerCase().contains("selection depth"));
	}

	@Test
	void pageSizeIsBoundedBeforePlanning() {
		var request = new SemanticRequest(CUSTOMER, List.of(new SemanticSelection(ID, null, List.of())),
				new SemanticQueryOptions(null, List.of(), 10, null, null), null);
		var d = SecurityResourceLimits.defaults();
		var limits = withPageSize(d, 5);
		var ex = assertThrows(IllegalStateException.class,
				() -> SecurityResourceLimitValidator.validate(request, limits));
		assertTrue(ex.getMessage().toLowerCase().contains("page size"));
	}

	@Test
	void orderPathDepthIsBounded() {
		var order = new SemanticOrderTerm(ID, SemanticSortDirection.ASC, List.of(ORDERS, ORDERS), null);
		var request = new SemanticRequest(CUSTOMER, List.of(new SemanticSelection(ID, null, List.of())),
				new SemanticQueryOptions(null, List.of(order), null, null, null), null);
		var d = SecurityResourceLimits.defaults();
		var limits = withOrderPathDepth(d, 1);
		var ex = assertThrows(IllegalStateException.class,
				() -> SecurityResourceLimitValidator.validate(request, limits));
		assertTrue(ex.getMessage().toLowerCase().contains("order relationship path"));
	}

	@Test
	void filterNodeCountIsBounded() {
		var filter = new SemanticAndFilter(List.of(new SemanticFieldFilter(ID, SemanticFilterOperator.EQ, 1),
				new SemanticFieldFilter(ID, SemanticFilterOperator.EQ, 2),
				new SemanticFieldFilter(ID, SemanticFilterOperator.EQ, 3)));
		var request = new SemanticRequest(CUSTOMER, List.of(new SemanticSelection(ID, null, List.of())),
				new SemanticQueryOptions(filter, List.of(), null, null, null), null);
		var d = SecurityResourceLimits.defaults();
		var limits = withFilterNodes(d, 2);
		var ex = assertThrows(IllegalStateException.class,
				() -> SecurityResourceLimitValidator.validate(request, limits));
		assertTrue(ex.getMessage().toLowerCase().contains("filter complexity"));
	}

	@Test
	void cursorLengthIsBounded() {
		var request = new SemanticRequest(CUSTOMER, List.of(new SemanticSelection(ID, null, List.of())),
				new SemanticQueryOptions(null, List.of(), null, null, "123456"), null);
		var d = SecurityResourceLimits.defaults();
		var limits = withCursorLength(d, 5);
		var ex = assertThrows(IllegalStateException.class,
				() -> SecurityResourceLimitValidator.validate(request, limits));
		assertTrue(ex.getMessage().toLowerCase().contains("cursor length"));
	}

	private static SecurityResourceLimits withPageSize(SecurityResourceLimits d, int v) {
		return copy(d, v, d.maxOffset(), d.maxCursorLength(), d.maxFilterNodes(), d.maxOrderPathDepth(),
				d.maxSelectionDepth());
	}

	private static SecurityResourceLimits withOrderPathDepth(SecurityResourceLimits d, int v) {
		return copy(d, d.maxPageSize(), d.maxOffset(), d.maxCursorLength(), d.maxFilterNodes(), v,
				d.maxSelectionDepth());
	}

	private static SecurityResourceLimits withFilterNodes(SecurityResourceLimits d, int v) {
		return copy(d, d.maxPageSize(), d.maxOffset(), d.maxCursorLength(), v, d.maxOrderPathDepth(),
				d.maxSelectionDepth());
	}

	private static SecurityResourceLimits withCursorLength(SecurityResourceLimits d, int v) {
		return copy(d, d.maxPageSize(), d.maxOffset(), v, d.maxFilterNodes(), d.maxOrderPathDepth(),
				d.maxSelectionDepth());
	}

	private static SecurityResourceLimits copy(SecurityResourceLimits d, int page, int offset, int cursor, int filter,
			int orderDepth, int selectionDepth) {
		return limits(selectionDepth, d.maxOperationGraphNodes(), d.maxOperationGraphDepth(),
				d.maxOperationGraphEdges(), d.maxOperationGraphFields(), d.maxSelectionNodes(), d.maxFilterDepth(),
				filter, d.maxOrderTerms(), orderDepth, page, offset, cursor, d.maxMutationOperations(),
				d.maxMutationFieldsPerOperation(), d.maxMutationReturnFieldsPerOperation(), d.maxMutationDependencies(),
				d.maxMutationEffects());
	}

	private static SecurityResourceLimits limits(int selectionDepth, int graphNodes, int graphDepth, int graphEdges,
			int graphFields, int selectionNodes, int filterDepth, int filterNodes, int orderTerms, int orderPathDepth,
			int pageSize, int offset, int cursorLength, int mutationOps, int mutationFields, int mutationReturns,
			int mutationDeps, int mutationEffects) {
		return new SecurityResourceLimits(selectionDepth, graphNodes, graphDepth, graphEdges, graphFields,
				selectionNodes, filterDepth, filterNodes, orderTerms, orderPathDepth, pageSize, offset, cursorLength,
				mutationOps, mutationFields, mutationReturns, mutationDeps, mutationEffects);
	}
}
