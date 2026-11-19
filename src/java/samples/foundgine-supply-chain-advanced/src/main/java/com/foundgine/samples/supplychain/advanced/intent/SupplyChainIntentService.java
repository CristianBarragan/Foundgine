package com.foundgine.samples.supplychain.advanced.intent;

import com.foundgine.core.abstractions.AuthorizationDecision;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionEvidence;
import com.foundgine.core.execution.ExecutionReceipt;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.ExecutionRow;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.SemanticGraph;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.core.semantic.intent.ReadIntent;
import com.foundgine.core.semantic.intent.ReadIntentCompiler;
import com.foundgine.core.semantic.planning.PlanInspectionNode;
import com.foundgine.core.semantic.resolution.SemanticRequestResolver;
import com.foundgine.core.serialization.JsonReadIntentAdapter;
import com.foundgine.providers.storage.inmemory.InMemoryCompiler;
import com.foundgine.providers.storage.inmemory.InMemoryExecutionProvider;
import com.foundgine.runtime.FoundgineServiceCollectionExtensions;
import com.foundgine.runtime.FoundgineServiceRegistry;
import com.foundgine.runtime.IFoundgine;
import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentMap;
import java.util.concurrent.ExecutionException;

/**
 * Transport-neutral canonical intent boundary for the Advanced sample.
 *
 * <p>The request is the canonical, provider-neutral {@code ReadIntent} JSON that core's {@link
 * JsonReadIntentAdapter} understands: a root entity, selections (fields and relationship names),
 * and optionally a filter, ordering and paging. It names <em>what is wanted</em> in the semantic
 * vocabulary (entity, field and relationship names or aliases), never tables, joins or SQL.
 *
 * <p>Everything else is Foundgine's job and happens inside a real engine: name resolution, path
 * resolution across relationships, authorization (entity, field, relationship and row-level
 * predicates), planning, execution and a receipt. The caller's identity is <em>not</em> part of the
 * intent; the host supplies it as an {@link IntentCaller}, which selects the authorization policy
 * and supplies the tenant to the row-level predicate. So one intent returns different results for
 * different callers, and nothing in the intent can widen what the caller may see.
 *
 * <p>Methods:
 *
 * <ul>
 *   <li>{@link #execute}: run the intent as the caller
 *   <li>{@link #explain}: dry-run it, showing how it resolves and what the caller's policy leaves
 *   <li>{@link #capabilities}: what the caller may read, by entity, field and relationship
 * </ul>
 *
 * <p>Data is a snapshot of the {@link SupplyChainData} fixture taken at construction.
 */
public final class SupplyChainIntentService {
    /** The execution-context key the tenant predicate reads ({@code context.TenantId}). */
    public static final String TENANT_CONTEXT_KEY = "context.TenantId";

    private static final SemanticModel MODEL = SupplyChainSemanticModel.MODEL;
    private static final SemanticContractSnapshot CONTRACT = MODEL.freeze().createSnapshot();
    private static final Duration EXECUTION_DEADLINE = Duration.ofSeconds(10);

    private final JsonReadIntentAdapter adapter = new JsonReadIntentAdapter();
    private final InMemoryCompiler compiler;
    private final EvidencingProvider provider;
    private final Set<String> knownTenants;
    // One engine per (tenant, role). The key space is bounded: tenants are validated against the
    // data and roles are an enum.
    private final ConcurrentMap<IntentCaller, IFoundgine> engines = new ConcurrentHashMap<>();

    public SupplyChainIntentService(SupplyChainData data) {
        var dataSet = SupplyChainRows.load(data);
        this.compiler = new InMemoryCompiler(SupplyChainSemanticModel.Metadata, dataSet);
        this.provider =
                new EvidencingProvider(
                        new InMemoryExecutionProvider(
                                SupplyChainSemanticModel.Metadata, dataSet));
        var tenants = new LinkedHashSet<String>();
        data.suppliers.forEach(s -> tenants.add(s.tenantId()));
        data.warehouses.forEach(w -> tenants.add(w.tenantId()));
        data.customers.forEach(c -> tenants.add(c.tenantId()));
        this.knownTenants = Set.copyOf(tenants);
    }

    /** Tenants present in the data; anything else is refused by {@link IntentCaller#resolve}. */
    public Set<String> knownTenants() {
        return knownTenants;
    }

    // ------------------------------------------------------------------ execute

    public CompletionStage<Map<String, Object>> execute(IntentCaller caller, String intentJson) {
        try {
            ReadIntent intent = parse(intentJson);
            var context =
                    new ExecutionContext(
                            Map.of(TENANT_CONTEXT_KEY, caller.tenantId()),
                            Instant.now().plus(EXECUTION_DEADLINE));
            return engine(caller)
                    .executeAsync(intent, context, CancellationToken.NONE)
                    .thenApply(result -> executionResponse(caller, result));
        } catch (Throwable t) {
            return CompletableFuture.failedFuture(t);
        }
    }

    private Map<String, Object> executionResponse(IntentCaller caller, ExecutionResult result) {
        var rows = new ArrayList<Map<String, Object>>();
        for (ExecutionRow row : result.rows()) rows.add(shapeRow(row));

        var response = new LinkedHashMap<String, Object>();
        response.put("caller", callerMap(caller));
        response.put("rowCount", rows.size());
        response.put("rows", rows);
        response.put("pageInfo", result.pageInfo());
        response.put("evidence", evidence(result.evidence()));
        response.put("receipt", receipt(result.receipt()));
        return response;
    }

    /** Keys rows by semantic name ({@code Entity.Field}) instead of provider-facing identities. */
    private static Map<String, Object> shapeRow(ExecutionRow row) {
        var shaped = new LinkedHashMap<String, Object>();
        for (var cell : row.effectiveCells().entrySet()) {
            var key = cell.getKey();
            SemanticEntity entity = MODEL.get(key.entityId());
            String name = entity.name() + "." + fieldName(entity, key.fieldId());
            if (shaped.containsKey(name)) name = name + "@" + key.nodeId();
            shaped.put(name, plain(cell.getValue()));
        }
        return shaped;
    }

    private static Object plain(Object value) {
        if (value == null || value instanceof Number || value instanceof Boolean || value instanceof String) {
            return value;
        }
        if (value instanceof Enum<?> e) return e.name();
        return value.toString();
    }

    private static Map<String, Object> evidence(ExecutionEvidence evidence) {
        if (evidence == null) return null;
        var map = new LinkedHashMap<String, Object>();
        map.put("provider", evidence.provider());
        map.put("planFingerprint", evidence.planFingerprint());
        map.put("authorizedNodeIds", evidence.authorizedNodeIds());
        map.put("rowsReturned", evidence.rowsReturned());
        map.put("elapsedMilliseconds", evidence.elapsedMilliseconds());
        map.put("intentFingerprint", evidence.intentFingerprint());
        map.put("authorizationFingerprint", evidence.authorizationFingerprint());
        return map;
    }

    private static Map<String, Object> receipt(ExecutionReceipt receipt) {
        if (receipt == null) return null;
        var map = new LinkedHashMap<String, Object>();
        map.put("requestId", receipt.requestId());
        map.put("status", receipt.status());
        map.put("provider", receipt.provider());
        map.put("intentFingerprint", receipt.intentFingerprint());
        map.put("planFingerprint", receipt.planFingerprint());
        map.put("authorizationFingerprint", receipt.authorizationFingerprint());
        map.put("startedAt", String.valueOf(receipt.startedAt()));
        map.put("completedAt", String.valueOf(receipt.completedAt()));
        map.put("elapsedMilliseconds", receipt.elapsedMilliseconds());
        return map;
    }

    // ------------------------------------------------------------------ explain

    /**
     * Plans and authorizes the intent without executing it. Shows how the intent's names resolved
     * (including the relationship path Foundgine chose), which requested fields the caller's policy
     * removed, and the authorized plan.
     */
    public Map<String, Object> explain(IntentCaller caller, String intentJson) {
        ReadIntent intent = parse(intentJson);
        var request = new ReadIntentCompiler(MODEL).compile(intent);
        SemanticGraph graph = new SemanticRequestResolver(CONTRACT).resolve(request);
        var dryRun = engine(caller).dryRun(request);
        var inspection = dryRun.inspection();

        var planNodes = new LinkedHashMap<Integer, PlanInspectionNode>();
        flatten(inspection.nodes(), planNodes);

        var resolution = new ArrayList<Map<String, Object>>();
        var pruned = new ArrayList<String>();
        for (var node : graph.nodes()) {
            SemanticEntity entity = MODEL.get(node.entityId());
            var requested = new ArrayList<String>();
            for (FieldId field : node.fields()) requested.add(entity.name() + "." + fieldName(entity, field));

            var step = new LinkedHashMap<String, Object>();
            step.put("node", node.id());
            step.put("entity", entity.name());
            if (node.viaRelationship() != null && node.parentId() != null) {
                var parent = graph.nodes().stream().filter(n -> n.id() == node.parentId()).findFirst();
                parent.ifPresent(p -> step.put("via", relationshipName(p.entityId(), node)));
            }
            step.put("fields", requested);
            resolution.add(step);

            var kept = planNodes.get(node.id());
            if (kept != null) {
                for (FieldId field : node.fields()) {
                    if (!kept.fieldIds().contains(field.value())) {
                        pruned.add(entity.name() + "." + fieldName(entity, field));
                    }
                }
            }
        }

        var effects = inspection.effects();
        var plan = new LinkedHashMap<String, Object>();
        plan.put("fingerprint", inspection.planFingerprint());
        plan.put("root", inspection.nodes().stream().map(SupplyChainIntentService::planNode).toList());
        plan.put(
                "effects",
                Map.of(
                        "hasWrites", effects.hasWrites(),
                        "hasExternalSideEffects", effects.hasExternalSideEffects(),
                        "affectedPlanNodes", effects.affectedPlanNodes(),
                        "effects", effects.effects()));

        var response = new LinkedHashMap<String, Object>();
        response.put("caller", callerMap(caller));
        response.put("contract", MODEL.contractFingerprint());
        response.put("resolution", resolution);
        response.put("authorization", Map.of("prunedFields", pruned));
        response.put("plan", plan);
        return response;
    }

    private static void flatten(List<PlanInspectionNode> nodes, Map<Integer, PlanInspectionNode> into) {
        for (var node : nodes) {
            into.put(node.nodeId(), node);
            flatten(node.children(), into);
        }
    }

    private static Map<String, Object> planNode(PlanInspectionNode node) {
        SemanticEntity entity = entityByValue(node.entityId());
        var map = new LinkedHashMap<String, Object>();
        map.put("node", node.nodeId());
        map.put("operation", node.operation());
        map.put("entity", entity == null ? String.valueOf(node.entityId()) : entity.name());
        var fields = new ArrayList<String>();
        for (long id : node.fieldIds()) {
            fields.add(entity == null ? String.valueOf(id) : entity.name() + "." + fieldName(entity, new FieldId(id)));
        }
        map.put("fields", fields);
        map.put("authorizationApplied", node.authorizationApplied());
        map.put("children", node.children().stream().map(SupplyChainIntentService::planNode).toList());
        return map;
    }

    // ------------------------------------------------------------------ capabilities

    /**
     * What the caller may do, as the authorization policy sees it: per entity, whether it is
     * readable (and whether a row-level predicate scopes it), and which fields and relationships
     * are visible. Descriptive only: execution evaluates the policy again.
     */
    public Map<String, Object> capabilities(IntentCaller caller) {
        var entities = new ArrayList<Map<String, Object>>();
        for (var capability : engine(caller).describeCapabilities().entities()) {
            SemanticEntity entity = MODEL.get(capability.entityId());
            var read = new LinkedHashMap<String, Object>();
            read.put("access", capability.read().access().name());
            // Discovery reports the access level only; CONDITIONAL means a row-level predicate
            // (here the tenant predicate) scopes the rows the caller can read.
            read.put(
                    "scopedByPredicate",
                    capability.read().access() == com.foundgine.core.abstractions.AuthorizationAccess.CONDITIONAL);
            read.put(
                    "fields",
                    capability.fields().stream()
                            .filter(f -> f.read().isAllowed())
                            .map(f -> f.name())
                            .toList());
            read.put(
                    "relationships",
                    capability.relationships().stream()
                            .filter(r -> r.read().isAllowed())
                            .map(r -> r.name())
                            .toList());

            var item = new LinkedHashMap<String, Object>();
            item.put("entity", capability.name());
            item.put("aliases", entity.effectiveAliases().stream().map(a -> a.name()).toList());
            item.put("read", read);
            item.put("write", Map.of("access", capability.write().access().name()));
            entities.add(item);
        }

        var shape = new LinkedHashMap<String, Object>();
        shape.put("rootEntity", "an entity name or alias");
        shape.put("selections", "[{field} | {relationship, children}]");
        shape.put("filter", "optional: field, relationship (exists/all/any) and boolean combinations");
        shape.put("order", "optional");
        shape.put("limit", "optional");
        shape.put("offset", "optional");
        shape.put("identity", "never part of the intent; supplied by the host as the caller");

        var response = new LinkedHashMap<String, Object>();
        response.put("caller", callerMap(caller));
        response.put("intentShape", shape);
        response.put("entities", entities);
        return response;
    }

    // ------------------------------------------------------------------ failures

    /** An error as a transport should report it. */
    public record Failure(int status, String type, String message) {}

    /** Classifies an exception from this service into a transport-neutral failure. */
    public static Failure failure(Throwable error) {
        Throwable t = error;
        while ((t instanceof CompletionException || t instanceof ExecutionException) && t.getCause() != null) {
            t = t.getCause();
        }
        String message = t.getMessage() == null ? t.getClass().getSimpleName() : t.getMessage();
        // SemanticAuthorizationException is an IllegalStateException, so it must be checked first.
        if (t instanceof SemanticAuthorizationException || t instanceof SecurityException) {
            return new Failure(403, "forbidden", semanticNames(message));
        }
        if (t instanceof InvalidIntentException || t instanceof IllegalArgumentException) {
            return new Failure(400, "invalid_intent", message);
        }
        if (message.contains("deadline")) {
            return new Failure(504, "timeout", "The intent did not finish within its deadline.");
        }
        // Never leak internals for anything unclassified.
        return new Failure(500, "internal_error", "The intent could not be executed.");
    }

    private static final java.util.regex.Pattern ENTITY_ID =
            java.util.regex.Pattern.compile("EntityId\\[value=(-?\\d+)\\]");

    /** The engine names entities by identity in denials; report the semantic name instead. */
    private static String semanticNames(String message) {
        return ENTITY_ID
                .matcher(message)
                .replaceAll(
                        m -> {
                            SemanticEntity entity = entityByValue(Long.parseLong(m.group(1)));
                            return java.util.regex.Matcher.quoteReplacement(
                                    entity == null ? "unknown" : entity.name());
                        });
    }

    /** The intent body was not a valid canonical read intent. */
    static final class InvalidIntentException extends RuntimeException {
        InvalidIntentException(String message) {
            super(message);
        }
    }

    // ------------------------------------------------------------------ helpers

    private ReadIntent parse(String json) {
        try {
            return adapter.parse(json);
        } catch (RuntimeException e) {
            throw new InvalidIntentException(e.getMessage());
        }
    }

    private IFoundgine engine(IntentCaller caller) {
        return engines.computeIfAbsent(
                caller,
                c -> {
                    var services = new FoundgineServiceRegistry();
                    FoundgineServiceCollectionExtensions.addFoundgine(
                            services,
                            MODEL,
                            SupplyChainAuthorization.create(c.tenantId(), c.role()),
                            compiler,
                            provider);
                    return services.getRequiredService(IFoundgine.class);
                });
    }

    private static Map<String, Object> callerMap(IntentCaller caller) {
        var map = new LinkedHashMap<String, Object>();
        map.put("tenant", caller.tenantId());
        map.put("role", caller.role().name());
        return map;
    }

    private static String fieldName(SemanticEntity entity, FieldId id) {
        return entity.fields().stream()
                .filter(f -> f.id().equals(id))
                .map(f -> f.name())
                .findFirst()
                .orElse(String.valueOf(id.value()));
    }

    private static String relationshipName(EntityId parentEntity, SemanticGraph.SemanticGraphNode node) {
        SemanticEntity parent = MODEL.get(parentEntity);
        String name =
                parent.relationships().stream()
                        .filter(r -> r.id().equals(node.viaRelationship()))
                        .map(r -> r.name())
                        .findFirst()
                        .orElse("?");
        return parent.name() + "." + name;
    }

    private static SemanticEntity entityByValue(long value) {
        return MODEL.entities().stream().filter(e -> e.id().value() == value).findFirst().orElse(null);
    }
}
