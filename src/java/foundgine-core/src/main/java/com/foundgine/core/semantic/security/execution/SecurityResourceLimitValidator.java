package com.foundgine.core.semantic.security.execution;

import com.foundgine.core.semantic.SemanticRequest;
import com.foundgine.core.semantic.SemanticSelection;
import com.foundgine.core.semantic.query.SemanticAggregateFilter;
import com.foundgine.core.semantic.query.SemanticAndFilter;
import com.foundgine.core.semantic.query.SemanticFilterExpression;
import com.foundgine.core.semantic.query.SemanticOrFilter;
import com.foundgine.core.semantic.query.SemanticOrderTerm;
import com.foundgine.core.semantic.query.SemanticQueryOptions;
import com.foundgine.core.semantic.query.SemanticRelationshipFilter;

import java.util.List;
import java.util.Objects;

/**
 * Enforces resource and complexity bounds at the semantic boundary.
 * Adapter-level limits are defense-in-depth; this validator is the canonical
 * engine-side guard and therefore also protects non-JSON callers.
 */
public final class SecurityResourceLimitValidator {

	private SecurityResourceLimitValidator() {
	}

	public static void validate(SemanticRequest request, SecurityResourceLimits limits) {
		Objects.requireNonNull(request);
		Objects.requireNonNull(limits);
		limits.validate();

		int selectionNodes = countSelections(request.selections(), 1, limits);
		if (selectionNodes > limits.maxSelectionNodes())
			reject("Selection complexity exceeds the configured maximum of " + limits.maxSelectionNodes() + " nodes.");

		SemanticQueryOptions options = request.options();
		if (options == null)
			return;

		if (options.limit() != null && options.limit() < 0)
			reject("Pagination values cannot be negative.");
		if (options.offset() != null && options.offset() < 0)
			reject("Pagination values cannot be negative.");
		if (options.limit() != null && options.limit() > 0 && options.limit() > limits.maxPageSize())
			reject("Requested page size exceeds the configured maximum of " + limits.maxPageSize() + ".");
		if (options.offset() != null && options.offset() > 0 && options.offset() > limits.maxOffset())
			reject("Requested offset exceeds the configured maximum of " + limits.maxOffset() + ".");
		if (options.after() != null && options.after().length() > limits.maxCursorLength())
			reject("Cursor length exceeds the configured maximum of " + limits.maxCursorLength() + ".");

		if (options.effectiveOrder().size() > limits.maxOrderTerms())
			reject("Order complexity exceeds the configured maximum of " + limits.maxOrderTerms() + " terms.");

		for (SemanticOrderTerm term : options.effectiveOrder()) {
			if (term.effectivePath().size() > limits.maxOrderPathDepth())
				reject("Order relationship path exceeds the configured maximum of " + limits.maxOrderPathDepth()
						+ " levels.");
		}

		if (options.filter() != null) {
			int filterNodes = countFilter(options.filter(), 1, limits);
			if (filterNodes > limits.maxFilterNodes())
				reject("Filter complexity exceeds the configured maximum of " + limits.maxFilterNodes() + " nodes.");
		}
	}

	private static int countSelections(List<SemanticSelection> selections, int depth, SecurityResourceLimits limits) {
		if (depth > limits.maxSelectionDepth())
			reject("Selection depth exceeds the configured maximum of " + limits.maxSelectionDepth() + ".");

		int count = 0;
		for (SemanticSelection selection : selections) {
			count++;
			if (count > limits.maxSelectionNodes())
				return count;

			if (selection.children() != null && !selection.children().isEmpty()) {
				count += countSelections(selection.children(), depth + 1, limits);
				if (count > limits.maxSelectionNodes())
					return count;
			}
		}

		return count;
	}

	public static void validateFilter(SemanticFilterExpression filter, SecurityResourceLimits limits) {
		countFilter(filter, 1, limits);
	}

	private static int countFilter(SemanticFilterExpression filter, int depth, SecurityResourceLimits limits) {
		if (depth > limits.maxFilterDepth())
			reject("Filter depth exceeds the configured maximum of " + limits.maxFilterDepth() + ".");

		int count = 1;
		if (filter instanceof SemanticRelationshipFilter relationship) {
			count += countFilter(relationship.predicate(), depth + 1, limits);
		} else if (filter instanceof SemanticAggregateFilter aggregate && aggregate.predicate() != null) {
			count += countFilter(aggregate.predicate(), depth + 1, limits);
		} else if (filter instanceof SemanticAndFilter and) {
			for (SemanticFilterExpression expression : and.expressions())
				count += countFilter(expression, depth + 1, limits);
		} else if (filter instanceof SemanticOrFilter or) {
			for (SemanticFilterExpression expression : or.expressions())
				count += countFilter(expression, depth + 1, limits);
		}

		return count;
	}

	private static void reject(String message) {
		throw new IllegalStateException(message);
	}
}
