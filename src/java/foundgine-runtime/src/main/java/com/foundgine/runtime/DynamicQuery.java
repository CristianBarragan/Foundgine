package com.foundgine.runtime;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.intent.ReadAndFilter;
import com.foundgine.core.semantic.intent.ReadFieldFilter;
import com.foundgine.core.semantic.intent.ReadFilter;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadOrFilter;
import com.foundgine.core.semantic.intent.ReadOrder;
import com.foundgine.core.semantic.intent.ReadRelationshipFilter;
import com.foundgine.core.semantic.intent.ReadSelection;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticRelationshipQuantifier;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.CompletionStage;
import java.util.function.Consumer;

/**
 * Port of {@code Foundgine.Runtime.DynamicQuery}.
 *
 * <p>
 * Open, fluent, string-keyed query authoring surface for entities not known at
 * compile time. Compiles to the same {@link ReadIntent} as {@link TypedQuery}.
 *
 * <p>
 * <b>Porting decision:</b> C#'s {@code Action<DynamicQuery> configure} callback
 * parameters ({@code Include}, {@code WhereRelated}) are ported as
 * {@link Consumer}, and {@code params string[] relationshipPath} is ported as a
 * Java varargs {@code String...} parameter, both direct equivalents.
 */
public final class DynamicQuery {

	private final IFoundgine foundgine;
	private final String entity;
	private final List<ReadSelection> selections = new ArrayList<>();
	private ReadFilter filter;
	private final List<ReadOrder> order = new ArrayList<>();
	private Integer limit;
	private Integer offset;
	private String after;
	private SecurityExecutionContext security;

	DynamicQuery(IFoundgine foundgine, String entity) {
		this.foundgine = Objects.requireNonNull(foundgine, "foundgine");
		if (entity == null || entity.isBlank())
			throw new IllegalArgumentException("entity must not be null or blank");
		this.entity = entity;
	}

	public DynamicQuery select(String... fields) {
		Objects.requireNonNull(fields, "fields");
		for (String field : fields) {
			if (field == null || field.isBlank())
				throw new IllegalArgumentException("field must not be null or blank");
			selections.add(new ReadSelection(field));
		}
		return this;
	}

	public DynamicQuery include(String relationship, Consumer<DynamicQuery> configure) {
		if (relationship == null || relationship.isBlank())
			throw new IllegalArgumentException("relationship must not be null or blank");
		Objects.requireNonNull(configure, "configure");
		DynamicQuery child = new DynamicQuery(foundgine, relationship);
		configure.accept(child);
		selections.add(new ReadSelection(null, relationship, List.copyOf(child.selections)));
		return this;
	}

	public DynamicQuery where(String field, SemanticFilterOperator op, Object value) {
		if (field == null || field.isBlank())
			throw new IllegalArgumentException("field must not be null or blank");
		this.filter = new ReadFieldFilter(field, op, value);
		return this;
	}

	public DynamicQuery whereRelated(String relationship, SemanticRelationshipQuantifier quantifier,
			Consumer<DynamicQuery> configure) {
		if (relationship == null || relationship.isBlank())
			throw new IllegalArgumentException("relationship must not be null or blank");
		Objects.requireNonNull(configure, "configure");
		DynamicQuery child = new DynamicQuery(foundgine, relationship);
		configure.accept(child);
		if (child.filter == null)
			throw new IllegalArgumentException("A related filter requires a child where() expression.");
		this.filter = new ReadRelationshipFilter(relationship, quantifier, child.filter);
		return this;
	}

	public DynamicQuery andWhere(String field, SemanticFilterOperator op, Object value) {
		ReadFieldFilter next = new ReadFieldFilter(field, op, value);
		this.filter = filter == null ? next : new ReadAndFilter(List.of(filter, next));
		return this;
	}

	public DynamicQuery orWhere(String field, SemanticFilterOperator op, Object value) {
		ReadFieldFilter next = new ReadFieldFilter(field, op, value);
		this.filter = filter == null ? next : new ReadOrFilter(List.of(filter, next));
		return this;
	}

	public DynamicQuery orderBy(String field, boolean descending) {
		if (field == null || field.isBlank())
			throw new IllegalArgumentException("field must not be null or blank");
		order.add(new ReadOrder(field, descending ? SemanticSortDirection.DESC : SemanticSortDirection.ASC));
		return this;
	}

	public DynamicQuery orderBy(String field) {
		return orderBy(field, false);
	}

	public DynamicQuery orderByPath(String field, boolean descending, String... relationshipPath) {
		if (field == null || field.isBlank())
			throw new IllegalArgumentException("field must not be null or blank");
		Objects.requireNonNull(relationshipPath, "relationshipPath");
		order.add(new ReadOrder(field, descending ? SemanticSortDirection.DESC : SemanticSortDirection.ASC,
				Arrays.asList(relationshipPath), null));
		return this;
	}

	public DynamicQuery take(int limit) {
		if (limit < 0)
			throw new IllegalArgumentException("limit must not be negative");
		this.limit = limit;
		return this;
	}

	public DynamicQuery skip(int offset) {
		if (offset < 0)
			throw new IllegalArgumentException("offset must not be negative");
		this.offset = offset;
		return this;
	}

	public DynamicQuery after(String cursor) {
		if (cursor == null || cursor.isBlank())
			throw new IllegalArgumentException("cursor must not be null or blank");
		this.after = cursor;
		return this;
	}

	public DynamicQuery withSecurity(SecurityExecutionContext security) {
		this.security = Objects.requireNonNull(security, "security");
		return this;
	}

	public CompletionStage<ExecutionResult> executeAsync(CancellationToken cancellationToken) {
		return foundgine.executeAsync(toIntent(), null, cancellationToken);
	}

	public CompletionStage<ExecutionResult> executeAsync() {
		return executeAsync(CancellationToken.NONE);
	}

	/** Returns the provider-neutral open intent without executing it. */
	public ReadIntent toIntent() {
		return new ReadIntent(entity, List.copyOf(selections), filter, List.copyOf(order), limit, offset, after,
				security);
	}
}
