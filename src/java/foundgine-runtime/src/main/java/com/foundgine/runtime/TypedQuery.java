package com.foundgine.runtime;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.semantic.intent.ReadAndFilter;
import com.foundgine.core.semantic.intent.ReadFieldFilter;
import com.foundgine.core.semantic.intent.ReadFilter;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadOrFilter;
import com.foundgine.core.semantic.intent.ReadOrder;
import com.foundgine.core.semantic.intent.ReadSelection;
import com.foundgine.core.semantic.query.SemanticFilterOperator;
import com.foundgine.core.semantic.query.SemanticSortDirection;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.CompletionStage;
import java.util.function.Consumer;

/**
 * Port of {@code Foundgine.Runtime.TypedQuery&lt;T&gt;}.
 *
 * <p>
 * Open, fluent query authoring surface. Typed and dynamic queries compile to
 * the same {@link ReadIntent}.
 *
 * <p>
 * <b>Porting decision:</b> C#'s {@code Select}, {@code Include}, {@code Where}
 * and {@code OrderBy} take {@code Expression<Func<T, ...>>} lambdas and pull
 * field names out of the expression tree at compile-inspection time (e.g.
 * {@code Select(x => x.Name)}). Java has no expression-tree equivalent — a Java
 * lambda is opaque bytecode by the time this class could inspect it — so every
 * one of those members is ported to take the semantic field or relationship
 * name directly as a {@code String} (or {@code
 * String...}) instead of a lambda. The type parameter {@code T} is kept purely
 * as call-site documentation of which entity the query targets (as it is
 * largely by C#, since {@code TProjection}/{@code TChild} are never otherwise
 * witnessed at runtime here either); it plays no role in validating the field
 * names, which — same as in {@link DynamicQuery} — are validated against the
 * semantic model at plan time, not at query-authoring time. Callers who want
 * compile-time-checked field names should generate per-entity constant classes
 * from the semantic model rather than relying on this builder to do so.
 *
 * <p>
 * {@code Include}'s two C# overloads (one for {@code
 * IEnumerable<TChild>} relationships, one for single {@code TChild}
 * relationships) collapse to a single Java overload, since both take only a
 * relationship name here.
 */
public final class TypedQuery<T> {

	private final IFoundgine foundgine;
	private final String entityName;
	private final List<ReadSelection> selections = new ArrayList<>();
	private ReadFilter filter;
	private final List<ReadOrder> order = new ArrayList<>();
	private Integer limit;
	private Integer offset;
	private String after;
	private SecurityExecutionContext security;

	TypedQuery(IFoundgine foundgine, String entityName) {
		this.foundgine = Objects.requireNonNull(foundgine, "foundgine");
		if (entityName == null || entityName.isBlank())
			throw new IllegalArgumentException("entityName must not be null or blank");
		this.entityName = entityName;
	}

	public TypedQuery<T> select(String... fields) {
		Objects.requireNonNull(fields, "fields");
		for (String field : fields) {
			if (field == null || field.isBlank())
				throw new IllegalArgumentException("field must not be null or blank");
			selections.add(new ReadSelection(field));
		}
		return this;
	}

	public <TChild> TypedQuery<T> include(String relationship, String childEntityName,
			Consumer<TypedQuery<TChild>> configure) {
		if (relationship == null || relationship.isBlank())
			throw new IllegalArgumentException("relationship must not be null or blank");
		Objects.requireNonNull(configure, "configure");
		TypedQuery<TChild> child = new TypedQuery<>(foundgine, childEntityName);
		configure.accept(child);
		selections.add(new ReadSelection(null, relationship, List.copyOf(child.selections)));
		return this;
	}

	public TypedQuery<T> where(String field, SemanticFilterOperator op, Object value) {
		if (field == null || field.isBlank())
			throw new IllegalArgumentException("field must not be null or blank");
		this.filter = new ReadFieldFilter(field, op, value);
		return this;
	}

	public TypedQuery<T> andWhere(String field, SemanticFilterOperator op, Object value) {
		ReadFieldFilter next = new ReadFieldFilter(field, op, value);
		this.filter = filter == null ? next : new ReadAndFilter(List.of(filter, next));
		return this;
	}

	public TypedQuery<T> orWhere(String field, SemanticFilterOperator op, Object value) {
		ReadFieldFilter next = new ReadFieldFilter(field, op, value);
		this.filter = filter == null ? next : new ReadOrFilter(List.of(filter, next));
		return this;
	}

	public TypedQuery<T> orderBy(String field, boolean descending) {
		if (field == null || field.isBlank())
			throw new IllegalArgumentException("field must not be null or blank");
		order.add(new ReadOrder(field, descending ? SemanticSortDirection.DESC : SemanticSortDirection.ASC));
		return this;
	}

	public TypedQuery<T> orderBy(String field) {
		return orderBy(field, false);
	}

	public TypedQuery<T> take(int limit) {
		if (limit < 0)
			throw new IllegalArgumentException("limit must not be negative");
		this.limit = limit;
		return this;
	}

	public TypedQuery<T> skip(int offset) {
		if (offset < 0)
			throw new IllegalArgumentException("offset must not be negative");
		this.offset = offset;
		return this;
	}

	public TypedQuery<T> after(String cursor) {
		if (cursor == null || cursor.isBlank())
			throw new IllegalArgumentException("cursor must not be null or blank");
		this.after = cursor;
		return this;
	}

	public TypedQuery<T> withSecurity(SecurityExecutionContext security) {
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
		return new ReadIntent(entityName, List.copyOf(selections), filter, List.copyOf(order), limit, offset, after,
				security);
	}
}
