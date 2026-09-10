package com.foundgine.core.execution;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticRelationship;
import com.foundgine.core.semantic.planning.SemanticPlan;
import com.foundgine.core.semantic.planning.SemanticPlanNode;
import com.foundgine.core.semantic.results.SemanticResult;
import com.foundgine.core.semantic.results.SemanticResultEvidence;
import com.foundgine.core.semantic.results.SemanticResultNode;
import com.foundgine.core.semantic.results.SemanticResultPageInfo;

import java.util.*;

/** Reconstructs a semantic result tree from flat provider rows. */
public final class ResultMaterializer {
    private final SemanticModel model;

    public ResultMaterializer(SemanticModel model) {
        this.model = Objects.requireNonNull(model, "model");
    }

    public SemanticResult materialize(SemanticPlan plan, ExecutionResult result) {
        Objects.requireNonNull(plan, "plan");
        Objects.requireNonNull(result, "result");
        var roots = new ArrayList<SemanticResultNode>();
        for (var row : result.rows()) addNode(plan.root(), row, roots, null);

        var page = result.pageInfo() == null ? null : new SemanticResultPageInfo(
                result.pageInfo().startCursor(), result.pageInfo().endCursor(),
                result.pageInfo().hasNextPage(), result.pageInfo().hasPreviousPage());
        var e = result.evidence();
        var evidence = e == null ? null : new SemanticResultEvidence(
                e.provider(), e.planFingerprint(), e.authorizedNodeIds(), e.rowsReturned(),
                e.elapsedMilliseconds(), e.providerOperationFingerprint(),
                e.intentFingerprint(), e.authorizationFingerprint());
        return new SemanticResult(roots, page, evidence);
    }

    private void addNode(SemanticPlanNode planNode, ExecutionRow row,
                         List<SemanticResultNode> siblings, SemanticResultNode parent) {
        SemanticEntity entity = model.get(planNode.entityId());
        FieldId identityField = entity.identity().fieldId();
        Object identityValue = getValue(row, planNode, identityField);
        if (identityValue == null) {
            if (parent != null) return;
            throw new IllegalStateException("Root entity '" + entity.name() + "' has a null identity value.");
        }

        SemanticResultNode node = null;
        for (var candidate : siblings) {
            if (candidate.entityId().equals(planNode.entityId())
                    && Objects.equals(candidate.identityValue(), identityValue)) {
                node = candidate;
                break;
            }
        }
        if (node == null) {
            var values = new LinkedHashMap<FieldId, Object>();
            for (var field : planNode.fields()) values.put(field, getValue(row, planNode, field));
            node = new SemanticResultNode(planNode.id(), planNode.entityId(), identityValue, values);
            siblings.add(node);
        }

        for (var child : planNode.children()) {
            RelationshipId relationshipId = child.viaRelationship();
            if (relationshipId == null)
                throw new IllegalStateException("Plan node " + child.id() + " has no relationship identity.");
            SemanticRelationship relationship = entity.relationships().stream()
                    .filter(x -> x.id().equals(relationshipId)).findFirst()
                    .orElseThrow(() -> new IllegalStateException(
                            "Entity '" + entity.name() + "' has no relationship '" + relationshipId + "'."));
            addNode(child, row, node.getChildren(relationship.id()), node);
        }
    }

    private static Object getValue(ExecutionRow row, SemanticPlanNode node, FieldId fieldId) {
        return row.effectiveCells().get(new ExecutionCellKey(node.id(), node.entityId(), fieldId));
    }
}
