package com.foundgine.samples.supplychain.advanced.postgres;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.*;
import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import java.sql.Connection;
import java.util.*;

/** Provider adapter that keeps PostgreSQL physical execution behind the Runtime mutation boundary. */
public final class PostgresAdvancedMutationProvider implements IMutationBatchExecutionProvider {
    private final PostgresSupplyChainStore store;
    private final Authorization.Context auth;
    public PostgresAdvancedMutationProvider(Connection connection, Authorization.Context auth) {
        this.store = new PostgresSupplyChainStore(connection); this.auth = Objects.requireNonNull(auth);
    }
    @Override public MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context) {
        var results = new ArrayList<MutationResult>();
        for (var op : ir.operations()) {
            var values = values(op);
            switch (op.entity().name()) {
                case "PlaceOrderCommand" -> {
                    var r = store.placeOrder((String) values.get("Actor"), auth, number(values,"CustomerId"), number(values,"ProductId"), number(values,"Quantity"), (String) values.get("IdempotencyKey"));
                    results.add(new MutationResult(1, Map.of(FieldId.create("PlaceOrderCommand","OrderId"), r.orderId())));
                }
                case "CancelOrderCommand" -> {
                    var r = store.cancelOrder((String) values.get("Actor"), auth, number(values,"OrderId"), (String) values.get("IdempotencyKey"));
                    results.add(new MutationResult(1, Map.of(FieldId.create("CancelOrderCommand","OrderId"), r.orderId())));
                }
                default -> throw new IllegalArgumentException("Unknown Advanced mutation command: " + op.entity().name());
            }
        }
        return new MutationBatchResult(results);
    }
    private static Map<String,Object> values(com.foundgine.core.semantic.planning.mutation.MutationOperation op) {
        var out=new LinkedHashMap<String,Object>();
        for(var f:op.fields()) {
            if(f.source()!=null) throw new UnsupportedOperationException("PostgreSQL command provider does not support correlated command fields.");
            out.put(knownFieldName(op.entity().name(), f.column()), f.value());
        }
        return out;
    }
    private static String knownFieldName(String entity, FieldId id) {
        for(String n:List.of("Actor","CustomerId","ProductId","Quantity","IdempotencyKey","OrderId"))
            if(FieldId.create(entity,n).equals(id)) return n;
        throw new IllegalArgumentException("Unknown command field "+id.value());
    }
    private static int number(Map<String,Object> values,String key){return ((Number)values.get(key)).intValue();}
}
