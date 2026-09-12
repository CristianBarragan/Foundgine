package com.foundgine.core.semantic.authorization;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.*;
import java.util.*;

/**
 * Validates canonical semantic IR against the trusted frozen contract before
 * policy evaluation.
 */
final class SemanticAuthorizationContractValidator {
	private SemanticAuthorizationContractValidator() {
	}

	static void validate(SemanticContractSnapshot contract, SemanticOperation operation) {
		Objects.requireNonNull(contract);
		Objects.requireNonNull(operation);
		validateNode(contract, operation.root(), new HashSet<>(), true, null);
	}

	private static void validateNode(SemanticContractSnapshot c, SemanticReadNode n, Set<Integer> visited, boolean root,
			SemanticEntity parent) {
		if (!visited.add(n.id()))
			throw new IllegalArgumentException(
					"Semantic operation contains a cycle or duplicate node at " + n.id() + ".");
		var entity = c.get(n.entityId());
		if (root && (n.viaRelationship() != null || n.viaConnection() != null))
			throw new IllegalArgumentException("Root semantic node " + n.id() + " cannot specify a parent edge.");
		if (!root && n.viaRelationship() == null && n.viaConnection() == null)
			throw new IllegalArgumentException("Non-root semantic node " + n.id()
					+ " must specify the relationship or connection used to reach it.");
		if (n.viaRelationship() != null) {
			if (n.viaConnection() != null)
				throw new IllegalArgumentException(
						"Semantic node " + n.id() + " cannot specify both a relationship and a connection.");
			if (parent != null) {
				var rel = parent.relationships().stream().filter(x -> x.id().equals(n.viaRelationship())).findFirst()
						.orElseThrow(() -> new IllegalArgumentException(
								"Semantic operation node " + n.id() + " references relationship '" + n.viaRelationship()
										+ "' not declared on '" + parent.name() + "'."));
				if (!rel.target().equals(n.entityId()))
					throw new IllegalStateException("Semantic operation node " + n.id() + " targets '" + n.entityId()
							+ "', but relationship '" + rel.name() + "' targets '" + rel.target() + "'.");
			}
		}
		var fields = new ArrayList<FieldId>();
		fields.addAll(n.fields());
		fields.addAll(n.requiredFields());
		for (var f : new LinkedHashSet<>(fields))
			if (!f.equals(entity.identity().fieldId()) && entity.fields().stream().noneMatch(x -> x.id().equals(f)))
				throw new IllegalArgumentException("Semantic operation node " + n.id() + " selects unknown field '" + f
						+ "' on '" + entity.name() + "'.");
		for (var child : n.children())
			validateNode(c, child, visited, false, entity);
	}
}
