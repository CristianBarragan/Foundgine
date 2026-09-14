package com.foundgine.core.execution.mutation;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Port of {@code Foundgine.Core.Execution.Mutation.MutationMaterializedNode} (a
 * mutable class in the C# source, not a record).
 */
public final class MutationMaterializedNode {

	private final Map<RelationshipId, List<MutationMaterializedNode>> children = new LinkedHashMap<>();

	private final int operationIndex;
	private final EntityId entityId;
	private final Map<FieldId, Object> values;

	public MutationMaterializedNode(int operationIndex, EntityId entityId, Map<FieldId, Object> values) {
		this.operationIndex = operationIndex;
		this.entityId = entityId;
		this.values = values;
	}

	public int operationIndex() {
		return operationIndex;
	}

	public EntityId entityId() {
		return entityId;
	}

	public Map<FieldId, Object> values() {
		return values;
	}

	/**
	 * Recomputed on every call, matching the C# {@code Children} property's
	 * {@code ToDictionary} behavior.
	 */
	public Map<RelationshipId, List<MutationMaterializedNode>> children() {
		Map<RelationshipId, List<MutationMaterializedNode>> snapshot = new LinkedHashMap<>();
		children.forEach((key, value) -> snapshot.put(key, List.copyOf(value)));
		return snapshot;
	}

	/**
	 * Package-private, matching the C# {@code internal} modifier: used by
	 * {@code MutationResultMaterializer} (not yet ported) within this package.
	 */
	List<MutationMaterializedNode> getChildren(RelationshipId relationshipId) {
		return children.computeIfAbsent(relationshipId, key -> new ArrayList<>());
	}
}
