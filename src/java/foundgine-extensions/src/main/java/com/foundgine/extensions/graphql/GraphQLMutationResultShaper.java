package com.foundgine.extensions.graphql;

import com.foundgine.core.execution.mutation.MutationMaterializedNode;
import com.foundgine.core.execution.mutation.MutationMaterializedResult;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/** Port of C# {@code GraphQLMutationResultShaper}. */
public final class GraphQLMutationResultShaper {
    private GraphQLMutationResultShaper() {}

    public static Map<String, Object> shape(MutationMaterializedNode node, GraphQLMutationResultShape shape) {
        Objects.requireNonNull(node, "node");
        Objects.requireNonNull(shape, "shape");

        Map<String, Object> result = new LinkedHashMap<>();
        for (var field : shape.fields()) result.put(field.responseName(), node.values().get(field.field()));

        for (var relationship : shape.relationships()) {
            var children = node.children().get(relationship.relationship());
            if (children == null) children = List.of();

            // C# drops children whose value bag is empty (zero affected rows).
            var materializedChildren = new ArrayList<MutationMaterializedNode>();
            for (var child : children) if (!child.values().isEmpty()) materializedChildren.add(child);

            if (relationship.isCollection()) {
                List<Object> shaped = new ArrayList<>(materializedChildren.size());
                for (var child : materializedChildren) shaped.add(shape(child, relationship.shape()));
                result.put(relationship.responseName(), shaped);
            } else {
                result.put(
                        relationship.responseName(),
                        materializedChildren.isEmpty() ? null : shape(materializedChildren.get(0), relationship.shape()));
            }
        }
        return result;
    }

    /** Null when there is no root or the root affected zero rows. */
    public static Map<String, Object> shapeRoot(MutationMaterializedResult materialized, GraphQLMutationResultShape shape) {
        Objects.requireNonNull(materialized, "materialized");
        Objects.requireNonNull(shape, "shape");

        if (materialized.roots().isEmpty()) return null;
        var root = materialized.roots().get(0);
        if (root == null || root.values().isEmpty()) return null;
        return shape(root, shape);
    }
}
