package com.foundgine.core.execution.mutation;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticRelationship;
import com.foundgine.core.semantic.planning.mutation.NestedMutationChild;
import com.foundgine.core.semantic.planning.mutation.NestedMutationIntent;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;

/**
 * Shapes a flat mutation batch result back into the semantic nested mutation
 * tree. Provider details and operation indexes remain internal to the result;
 * callers consume entity/field values and relationship children.
 */
public final class MutationResultMaterializer {

    /**
     * C# represents a batch item as the value-tuple {@code (string Key, NestedMutationIntent Intent)}
     * and the batch output as {@code (string Key, MutationMaterializedResult Result)}; Java has no
     * built-in tuple type, so both are ported as small nested records (same treatment as
     * {@code MutationDependencyGraph.Edge}).
     */
    public record Item(String key, NestedMutationIntent intent) {
    }

    public record KeyedResult(String key, MutationMaterializedResult result) {
    }

    private final SemanticModel model;

    public MutationResultMaterializer(SemanticModel model) {
        this.model = Objects.requireNonNull(model);
    }

    public MutationMaterializedResult materialize(NestedMutationIntent intent, MutationBatchResult result) {
        Objects.requireNonNull(intent);
        Objects.requireNonNull(result);

        int expectedOperations = count(intent);
        if (result.results().size() != expectedOperations) {
            throw new IllegalStateException(
                    "Mutation result contains " + result.results().size() + " operations, but the nested mutation contains "
                            + expectedOperations + ".");
        }

        int[] nextOperation = {0};
        List<MutationMaterializedNode> roots = new ArrayList<>(1);
        addNode(intent, result.results(), nextOperation, roots);

        if (nextOperation[0] != result.results().size())
            throw new IllegalStateException("Nested mutation result did not consume every operation result.");

        return new MutationMaterializedResult(roots);
    }

    private void addNode(
            NestedMutationIntent intent,
            List<MutationResult> results,
            int[] operationIndex,
            List<MutationMaterializedNode> siblings) {
        int currentIndex = operationIndex[0]++;
        SemanticEntity entity = model.get(intent.mutation().entity());
        MutationResult result = results.get(currentIndex);
        Map<FieldId, Object> values = result.returnedValues() == null
                ? new LinkedHashMap<>()
                : new LinkedHashMap<>(result.returnedValues());

        MutationMaterializedNode node = new MutationMaterializedNode(currentIndex, entity.id(), values);
        siblings.add(node);

        for (NestedMutationChild child : intent.children()) {
            SemanticRelationship relationship = entity.relationships().stream()
                    .filter(x -> x.id().equals(child.relationship()))
                    .findFirst()
                    .orElseThrow(() -> new IllegalStateException(
                            "Entity '" + entity.name() + "' has no relationship '" + child.relationship().value() + "'."));

            if (!relationship.target().equals(child.mutation().mutation().entity())) {
                throw new IllegalStateException(
                        "Relationship '" + relationship.name() + "' targets '" + relationship.target().value()
                                + "', but nested result targets '" + child.mutation().mutation().entity().value() + "'.");
            }

            addNode(child.mutation(), results, operationIndex, node.getChildren(relationship.id()));
        }
    }

    private static int count(NestedMutationIntent intent) {
        int total = 1;
        for (NestedMutationChild child : intent.children())
            total += count(child.mutation());
        return total;
    }

    /**
     * Batch form of {@link #materialize}: takes N independent (key, intent) pairs — e.g. one per
     * aliased root field from a HotChocolate mutation adapter combined into one plan via
     * {@code MutationPlanner.plan(List<NestedMutationIntent>)} — and ONE flat
     * {@link MutationBatchResult} produced by executing that combined plan. Slices
     * {@code result.results()} back into per-item chunks (same order the planner concatenated
     * them in) and materializes each independently, so nested children within one item still
     * resolve correctly.
     */
    public List<KeyedResult> materializeBatch(List<Item> items, MutationBatchResult result) {
        Objects.requireNonNull(items);
        Objects.requireNonNull(result);
        if (items.isEmpty())
            throw new IllegalStateException("A materialized mutation batch must contain at least one item.");

        List<KeyedResult> output = new ArrayList<>(items.size());
        int offset = 0;
        for (Item item : items) {
            int count = count(item.intent());
            if (offset + count > result.results().size()) {
                throw new IllegalStateException(
                        "Batched mutation result has " + result.results().size() + " operations total, but item '"
                                + item.key() + "' alone needs operations " + offset + ".." + (offset + count - 1) + ".");
            }

            MutationBatchResult slice = new MutationBatchResult(result.results().subList(offset, offset + count));
            output.add(new KeyedResult(item.key(), materialize(item.intent(), slice)));
            offset += count;
        }

        if (offset != result.results().size()) {
            throw new IllegalStateException(
                    "Batched mutation result contains " + result.results().size()
                            + " operations, but the batch items only account for " + offset + ".");
        }

        return output;
    }
}
