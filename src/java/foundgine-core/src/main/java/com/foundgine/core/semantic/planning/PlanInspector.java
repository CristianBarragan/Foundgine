package com.foundgine.core.semantic.planning;

import java.util.*;

/** Builds an inspection artifact directly from the canonical semantic plan. */
public final class PlanInspector {
	private PlanInspector() {
	}

	public static PlanInspection inspect(SemanticPlan plan) {
		Objects.requireNonNull(plan);
		var nodes = new ArrayList<SemanticPlanNode>();
		flatten(plan.root(), nodes);
		var inspectionNodes = nodes.stream().map(PlanInspector::toInspection).toList();
		return new PlanInspection(plan, SemanticPlanFingerprint.create(plan), inspectionNodes,
				new PlanEffectSummary(false, false, nodes.size(), List.of()));
	}

	private static void flatten(SemanticPlanNode n, List<SemanticPlanNode> out) {
		out.add(n);
		n.children().forEach(c -> flatten(c, out));
	}

	private static PlanInspectionNode toInspection(SemanticPlanNode n) {
		return new PlanInspectionNode(n.id(), n.operation().name(), n.entityId().value(),
				n.fields().stream().map(x -> x.value()).toList(),
				n.viaRelationship() == null ? null : n.viaRelationship().value(),
				n.viaConnection() == null ? null : n.viaConnection().value(), n.authorization() != null,
				n.children().stream().map(PlanInspector::toInspection).toList());
	}
}
