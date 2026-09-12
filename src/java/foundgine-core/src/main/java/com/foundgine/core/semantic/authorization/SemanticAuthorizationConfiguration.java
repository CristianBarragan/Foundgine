package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.*;
import java.util.*;

/** Reusable provider-independent semantic authorization configuration. */
public final class SemanticAuthorizationConfiguration {
	@FunctionalInterface
	public interface EntityRule {
		boolean test(SemanticAuthorizationContext c, EntityId e, AuthorizationOperation o);
	}

	@FunctionalInterface
	public interface FieldRule {
		boolean test(SemanticAuthorizationContext c, EntityId e, FieldId f, AuthorizationOperation o);
	}

	@FunctionalInterface
	public interface RelationshipRule {
		boolean test(SemanticAuthorizationContext c, EntityId e, RelationshipId r, AuthorizationOperation o);
	}

	@FunctionalInterface
	public interface PredicateRule {
		AuthorizationPredicate apply(SemanticAuthorizationContext c, EntityId e, AuthorizationOperation o);
	}

	@FunctionalInterface
	public interface OperationRule {
		AuthorizationDecision apply(SemanticAuthorizationContext c, EntityId e, AuthorizationOperation o,
				AuthorizationOperationName n);
	}

	private final List<EntityRule> entities = new ArrayList<>();
	private final List<FieldRule> fields = new ArrayList<>();
	private final List<RelationshipRule> relationships = new ArrayList<>();
	private final List<PredicateRule> predicates = new ArrayList<>();
	private final List<OperationRule> operations = new ArrayList<>();

	public SemanticAuthorizationConfiguration allowAll() {
		return addEntityRule((c, e, o) -> true).addFieldRule((c, e, f, o) -> true)
				.addRelationshipRule((c, e, r, o) -> true);
	}

	public SemanticAuthorizationConfiguration addEntityRule(EntityRule r) {
		entities.add(Objects.requireNonNull(r));
		return this;
	}

	public SemanticAuthorizationConfiguration addFieldRule(FieldRule r) {
		fields.add(Objects.requireNonNull(r));
		return this;
	}

	public SemanticAuthorizationConfiguration addRelationshipRule(RelationshipRule r) {
		relationships.add(Objects.requireNonNull(r));
		return this;
	}

	public SemanticAuthorizationConfiguration addPredicateRule(PredicateRule r) {
		predicates.add(Objects.requireNonNull(r));
		return this;
	}

	public SemanticAuthorizationConfiguration addOperationRule(OperationRule r) {
		operations.add(Objects.requireNonNull(r));
		return this;
	}

	boolean canAccessEntity(SemanticAuthorizationContext c, EntityId e, AuthorizationOperation o) {
		return !entities.isEmpty() && entities.stream().allMatch(r -> r.test(c, e, o));
	}

	boolean canAccessField(SemanticAuthorizationContext c, EntityId e, FieldId f, AuthorizationOperation o) {
		return !fields.isEmpty() && fields.stream().allMatch(r -> r.test(c, e, f, o));
	}

	boolean canAccessRelationship(SemanticAuthorizationContext c, EntityId e, RelationshipId r,
			AuthorizationOperation o) {
		return !relationships.isEmpty() && relationships.stream().allMatch(x -> x.test(c, e, r, o));
	}

	AuthorizationPredicate getPredicate(SemanticAuthorizationContext c, EntityId e, AuthorizationOperation o) {
		AuthorizationPredicate result = null;
		for (var r : predicates) {
			var x = r.apply(c, e, o);
			if (x != null)
				result = result == null ? x : AuthorizationPredicate.and(result, x);
		}
		return result;
	}

	AuthorizationDecision getOperationDecision(SemanticAuthorizationContext c, EntityId e, AuthorizationOperation o,
			AuthorizationOperationName n) {
		AuthorizationDecision result = null;
		for (var r : operations) {
			var x = r.apply(c, e, o, n);
			if (x != null)
				result = result == null ? x : AuthorizationDecision.combine(result, x);
		}
		return result;
	}
}
