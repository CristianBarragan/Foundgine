package com.foundgine.providers.storage.orm;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionCellKey;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionEvidenceFactory;
import com.foundgine.core.execution.ExecutionIRNode;
import com.foundgine.core.execution.ExecutionPageInfo;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.execution.IExecutionProvider;
import com.foundgine.core.execution.ProviderPlan;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;

/**
 * Executes ExecutionIR through an {@link IOrmQuerySource}. One query per plan node; children are
 * fetched with the page's parent keys (chunked) after their own authorization. The result is the
 * flat LEFT-JOIN-shaped row set {@code ResultMaterializer} consumes. Reads are blocking: run on a
 * thread that may block (the returned stage is already completed).
 */
public class OrmExecutionProvider implements IExecutionProvider {
    private final String provider;
    private final IOrmQuerySource source;
    private final OrmRegistry registry;

    public OrmExecutionProvider(String provider, IOrmQuerySource source, OrmRegistry registry) {
        this.provider = Objects.requireNonNull(provider, "provider");
        this.source = Objects.requireNonNull(source, "source");
        this.registry = Objects.requireNonNull(registry, "registry");
    }

    @Override
    public CompletionStage<ExecutionResult> executeAsync(
            ProviderPlan plan, ExecutionContext context, CancellationToken cancellationToken) {
        return CompletableFuture.completedFuture(execute(plan, context, cancellationToken));
    }

    public ExecutionResult execute(
            ProviderPlan plan, ExecutionContext context, CancellationToken token) {
        Objects.requireNonNull(plan, "plan");
        Objects.requireNonNull(context, "context");
        if (!(plan instanceof OrmPlan ormPlan) || !ormPlan.provider().equals(provider)) {
            throw new IllegalArgumentException(
                    "The " + provider + " provider requires a " + provider + " OrmPlan.");
        }
        if (ormPlan.authorizationBinding() == null) {
            throw new IllegalStateException(
                    "The "
                            + provider
                            + " provider refuses to execute a provider plan without authorization"
                            + " provenance.");
        }
        context.ensureWithinDeadline();
        long started = System.nanoTime();

        var root = ormPlan.ir().root();
        var tree = load(root, null, null, context, token);
        var rows = new ArrayList<ExecutionRow>();
        for (int i = 0; i < tree.rows.size(); i++) {
            for (var joined : expand(tree, i, true)) {
                rows.add(new ExecutionRow(joined.values, joined.cells));
            }
        }
        context.ensureWithinDeadline();
        long elapsed = (System.nanoTime() - started) / 1_000_000L;

        String fingerprint = fingerprint(ormPlan);
        var authorized = new ArrayList<Integer>();
        collectAuthorized(root, authorized);
        var evidence =
                ExecutionEvidenceFactory.create(
                        provider,
                        ExecutionEvidenceFactory.hash(fingerprint),
                        authorized,
                        rows.size(),
                        elapsed,
                        fingerprint);
        return new ExecutionResult(rows, tree.pageInfo, evidence, null);
    }

    // ------------------------------------------------------------------ loading

    private static final class NodeData {
        ExecutionIRNode node;
        List<Map<FieldId, Object>> rows = List.of();
        ExecutionPageInfo pageInfo;
        final List<ChildData> children = new ArrayList<>();
    }

    private static final class ChildData {
        OrmLink link;
        NodeData data;
        final Map<String, List<Integer>> byKey = new HashMap<>();
    }

    private NodeData load(
            ExecutionIRNode node,
            OrmLink incoming,
            List<Object> keys,
            ExecutionContext context,
            CancellationToken token) {
        token.throwIfCancellationRequested();
        context.ensureWithinDeadline();
        var map = registry.map(node.entityId());

        // Key fields are read only to join; they are emitted only if the node selected them.
        var extra = new ArrayList<FieldId>();
        if (incoming != null) extra.add(incoming.childField());
        var links = new ArrayList<OrmLink>(node.children().size());
        for (var child : node.children()) {
            var link = registry.link(child.viaRelationship(), node.entityId(), child.entityId());
            links.add(link);
            if (!extra.contains(link.parentField())) extra.add(link.parentField());
        }

        OrmQueries.Fetch fetch;
        if (incoming != null && keys != null && keys.isEmpty()) {
            OrmQueries.validate(registry, map, node, false);
            fetch = new OrmQueries.Fetch(List.of(), null, List.of()); // no parent keys: nothing can match
        } else {
            fetch =
                    OrmQueries.fetch(
                            registry,
                            map,
                            node,
                            incoming == null,
                            extra,
                            incoming == null ? null : incoming.childField(),
                            keys,
                            source,
                            context);
        }

        var data = new NodeData();
        data.node = node;
        data.rows = fetch.rows();
        data.pageInfo = fetch.pageInfo();
        for (int c = 0; c < node.children().size(); c++) {
            var link = links.get(c);
            var parentKeys = new ArrayList<Object>();
            Set<String> seen = new HashSet<>();
            for (var row : data.rows) {
                Object key = row.get(link.parentField());
                String normalized = OrmValues.normalize(key);
                if (normalized != null && seen.add(normalized)) parentKeys.add(key);
            }
            var childData = load(node.children().get(c), link, parentKeys, context, token);
            var child = new ChildData();
            child.link = link;
            child.data = childData;
            for (int i = 0; i < childData.rows.size(); i++) {
                String k = OrmValues.normalize(childData.rows.get(i).get(link.childField()));
                if (k == null) continue;
                child.byKey.computeIfAbsent(k, x -> new ArrayList<>()).add(i);
            }
            data.children.add(child);
        }
        return data;
    }

    // ------------------------------------------------------------------ row assembly

    private static final class Joined {
        final Map<ExecutionCellKey, Object> cells = new LinkedHashMap<>();
        final Map<String, Object> values = new LinkedHashMap<>();
    }

    private List<Joined> expand(NodeData data, int rowIndex, boolean isRoot) {
        var node = data.node;
        var row = data.rows.get(rowIndex);
        var map = registry.map(node.entityId());

        var own = new Joined();
        for (FieldId fieldId : node.fields()) {
            Object value = row.get(fieldId);
            own.cells.put(new ExecutionCellKey(node.id(), node.entityId(), fieldId), value);
            own.values.put((isRoot ? "" : node.id() + ".") + map.field(fieldId).name(), value);
        }

        List<Joined> combos = new ArrayList<>(List.of(own));
        for (var child : data.children) {
            var matches = new ArrayList<Joined>();
            String key = OrmValues.normalize(row.get(child.link.parentField()));
            var indexes = key == null ? null : child.byKey.get(key);
            if (indexes != null) {
                for (int i : indexes) matches.addAll(expand(child.data, i, false));
            }
            if (matches.isEmpty()) continue; // left join: keep the row, child cells stay absent

            var next = new ArrayList<Joined>(combos.size() * matches.size());
            for (var left : combos) {
                for (var right : matches) {
                    var merged = new Joined();
                    merged.cells.putAll(left.cells);
                    merged.cells.putAll(right.cells);
                    merged.values.putAll(left.values);
                    merged.values.putAll(right.values);
                    next.add(merged);
                }
            }
            combos = next;
        }
        return combos;
    }

    // ------------------------------------------------------------------ evidence

    private static void collectAuthorized(ExecutionIRNode node, List<Integer> ids) {
        if (node.authorization() != null) ids.add(node.id());
        for (var child : node.children()) collectAuthorized(child, ids);
    }

    private static String fingerprint(OrmPlan plan) {
        var sb = new StringBuilder(plan.provider());
        append(sb, plan.ir().root());
        return sb.toString();
    }

    private static void append(StringBuilder sb, ExecutionIRNode n) {
        var o = n.queryOptions();
        sb.append("|node=").append(n.id())
                .append("|entity=").append(n.entityId().value())
                .append("|via=").append(n.viaRelationship() == null ? null : n.viaRelationship().value())
                .append("|fields=")
                .append(String.join(",", n.fields().stream().map(f -> Long.toString(f.value())).toList()))
                .append("|order=")
                .append(
                        o == null
                                ? ""
                                : String.join(
                                        ",",
                                        o.effectiveOrder().stream()
                                                .map(t -> t.field().value() + ":" + t.direction() + ":" + t.aggregate() + ":" + t.effectivePath().stream().map(x -> Long.toString(x.value())).toList())
                                                .toList()))
                .append("|limit=").append(o == null ? null : o.limit())
                .append("|offset=").append(o == null ? null : o.offset())
                .append("|after=").append(o != null && o.hasCursor())
                .append("|filter=").append(o != null && o.filter() != null)
                .append("|auth=").append(n.authorization() != null);
        if (n.children().isEmpty()) return;
        sb.append('[');
        for (var child : n.children()) append(sb, child);
        sb.append(']');
    }
}
