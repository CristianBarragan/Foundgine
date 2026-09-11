package com.foundgine.samples.supplychain.advanced.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.*;
import com.foundgine.core.semantic.SemanticModel;
import com.foundgine.core.semantic.SemanticEntity;
import com.foundgine.core.semantic.mutation.*;
import com.foundgine.runtime.*;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import com.foundgine.samples.supplychain.advanced.authorization.SupplyChainAuthorization;
import com.foundgine.samples.supplychain.advanced.data.SupplyChainData;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import java.util.*;
import java.util.concurrent.CompletionStage;

/**
 * End-to-end Advanced sample mutation boundary:
 * Intent -> semantic planning -> authorization -> execution IR -> provider -> domain mutation.
 * The provider is deliberately an application adapter; the runtime remains provider-neutral.
 */
public final class AdvancedMutationPipeline {
    public static final EntityId PLACE_ORDER = EntityId.create("PlaceOrderCommand");
    public static final EntityId CANCEL_ORDER = EntityId.create("CancelOrderCommand");

    private final SupplyChainData data;
    private final Authorization.Context auth;
    private final FoundgineMutationEngine engine;

    public AdvancedMutationPipeline(SupplyChainData data, Authorization.Context auth) {
        this(data, auth, new DomainMutationProvider(data, auth));
    }

    public AdvancedMutationPipeline(SupplyChainData data, Authorization.Context auth,
                                    IMutationBatchExecutionProvider provider) {
        this.data = Objects.requireNonNull(data);
        this.auth = Objects.requireNonNull(auth);
        var model = SupplyChainSemanticModel.MODEL;
        var schema = schema(model);
        var policy = SupplyChainAuthorization.create(auth.tenantId(),
                SupplyChainAuthorization.Role.valueOf(auth.role().name()), Map.of());
        this.engine = new FoundgineMutationEngine(schema, policy,
                Objects.requireNonNull(provider), model,
                null, null, null, null);
    }

    public CompletionStage<MutationExecutionResult> placeOrder(String actor, int customerId, int productId,
                                                                int quantity, String idempotencyKey) {
        var op = new SemanticMutationOperation(
                PLACE_ORDER, SemanticMutationKind.CREATE,
                List.of(value(PLACE_ORDER, "Actor", actor), value(PLACE_ORDER, "CustomerId", customerId), value(PLACE_ORDER, "ProductId", productId),
                        value(PLACE_ORDER, "Quantity", quantity), value(PLACE_ORDER, "IdempotencyKey", idempotencyKey)),
                null, List.of(), List.of(field(PLACE_ORDER, "OrderId")), List.of(), List.of());
        return engine.executeAsync(new SemanticMutationRequest(new SemanticMutationOperationGraph(List.of(op))),
                ExecutionContext.EMPTY, CancellationToken.NONE);
    }

    public CompletionStage<MutationExecutionResult> cancelOrder(String actor, int orderId, String idempotencyKey) {
        var op = new SemanticMutationOperation(
                CANCEL_ORDER, SemanticMutationKind.CREATE,
                List.of(value(CANCEL_ORDER, "Actor", actor), value(CANCEL_ORDER, "OrderId", orderId), value(CANCEL_ORDER, "IdempotencyKey", idempotencyKey)),
                null, List.of(), List.of(field(CANCEL_ORDER, "OrderId")), List.of(), List.of());
        return engine.executeAsync(new SemanticMutationRequest(new SemanticMutationOperationGraph(List.of(op))),
                ExecutionContext.EMPTY, CancellationToken.NONE);
    }

    private static SemanticMutationField value(EntityId entity, String name, Object value) {
        return new SemanticMutationField(field(entity, name), value);
    }

    private static FieldId field(EntityId entity, String name) { return FieldId.create(entity == PLACE_ORDER ? "PlaceOrderCommand" : "CancelOrderCommand", name); }
    private static FieldId field(EntityId entity, String name, boolean ignored) { return field(entity, name); }

    private static MutationSchema schema(SemanticModel model) {
        return new MutationSchema() {
            @Override public MutationEntitySchema getEntity(EntityId id) {
                SemanticEntity e = model.get(id);
                var fields = new LinkedHashMap<FieldId, ColumnId>();
                var columns = new LinkedHashSet<ColumnId>();
                for (var f : e.fields()) {
                    var c = ColumnId.create(e.name(), f.name()); fields.put(f.id(), c); columns.add(c);
                }
                FieldId identity = e.identity().fieldId();
                return new MutationEntitySchema(id, e.name(), columns, fields,
                        identity == null ? null : fields.get(identity));
            }
            @Override public MutationRelationshipSchema getRelationship(RelationshipId id) {
                throw new UnsupportedOperationException("Command mutations do not use relationship filters.");
            }
        };
    }

    private static final class DomainMutationProvider implements IMutationBatchExecutionProvider {
        private final SupplyChainData data; private final Authorization.Context auth;
        DomainMutationProvider(SupplyChainData data, Authorization.Context auth) { this.data = data; this.auth = auth; }

        @Override public MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context) {
            var results = new ArrayList<MutationResult>();
            for (var op : ir.operations()) {
                if (op.kind() != com.foundgine.core.semantic.planning.mutation.MutationKind.CREATE)
                    throw new IllegalArgumentException("Advanced command provider only accepts CREATE command operations.");
                var values = values(op);
                String name = op.entity().name();
                if ("PlaceOrderCommand".equals(name)) {
                    var service = new PlaceOrderService(data);
                    var r = service.placeOrder((String) values.get("Actor"), auth,
                            number(values, "CustomerId"), List.of(new PlaceOrderService.OrderLine(
                                    number(values, "ProductId"), number(values, "Quantity"))),
                            (String) values.get("IdempotencyKey"));
                    results.add(new MutationResult(1, Map.of(field(PLACE_ORDER, "OrderId"), r.orderId())));
                } else if ("CancelOrderCommand".equals(name)) {
                    var service = new CancelOrderService(data);
                    var r = service.cancelOrder((String) values.get("Actor"), auth,
                            number(values, "OrderId"), (String) values.get("IdempotencyKey"));
                    results.add(new MutationResult(1, Map.of(field(CANCEL_ORDER, "OrderId"), r.orderId())));
                } else throw new IllegalArgumentException("Unknown Advanced mutation command: " + name);
            }
            return new MutationBatchResult(results);
        }

        private static Map<String,Object> values(com.foundgine.core.semantic.planning.mutation.MutationOperation op) {
            var out = new LinkedHashMap<String,Object>();
            for (var f : op.fields()) {
                if (f.source() != null) throw new UnsupportedOperationException("Command provider does not support field correlation yet.");
                out.put(fieldName(op.entity(), f.column()), f.value());
            }
            return out;
        }
        private static String fieldName(MutationEntitySchema entity, ColumnId column) {
            return entity.fields().entrySet().stream()
                    .filter(e -> e.getValue().equals(column))
                    .map(e -> knownFieldName(entity.name(), e.getKey()))
                    .findFirst().orElseThrow();
        }
        private static String knownFieldName(String entity, FieldId id) {
            for (String name : List.of("Actor", "CustomerId", "ProductId", "Quantity", "IdempotencyKey", "OrderId"))
                if (FieldId.create(entity, name).equals(id)) return name;
            throw new IllegalArgumentException("Unknown command field " + id.value());
        }
        private static int number(Map<String,Object> values, String key) { return ((Number) values.get(key)).intValue(); }
    }
}
