package com.foundgine.providers.storage.sql;

import com.foundgine.core.execution.*;
import com.foundgine.providers.storage.sql.query.CursorCodec;
import com.foundgine.providers.storage.sql.query.SqlParameterBinding;
import java.sql.*;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;

/** Executes an already-compiled SQL plan against a JDBC connection. */
public final class SqlExecutionProvider implements IExecutionProvider {
    private final Connection connection;
    private final Transaction transaction;

    /** Optional transaction handle used to preserve the provider's transaction boundary. */
    public interface Transaction { void apply(Statement statement) throws SQLException; }

    public SqlExecutionProvider(Connection connection) { this(connection, null); }
    public SqlExecutionProvider(Connection connection, Transaction transaction) {
        this.connection = Objects.requireNonNull(connection, "connection"); this.transaction = transaction;
    }

    @Override
    public CompletionStage<ExecutionResult> executeAsync(ProviderPlan plan, ExecutionContext context, CancellationToken token) {
        Objects.requireNonNull(plan, "plan"); Objects.requireNonNull(context, "context"); Objects.requireNonNull(token, "token");
        if (!(plan instanceof SqlPlan sqlPlan)) return CompletableFuture.failedFuture(new IllegalArgumentException("The SQL provider requires a SqlPlan."));
        if (sqlPlan.authorizationBinding() == null) return CompletableFuture.failedFuture(new IllegalStateException("The SQL provider refuses to execute a provider plan without authorization provenance."));
        try {
            token.throwIfCancellationRequested();
            context.ensureWithinDeadline();
            long start = System.nanoTime();
            if (connection.isClosed()) throw new SQLException("The JDBC connection is closed.");
            try (PreparedStatement statement = connection.prepareStatement(sqlPlan.commandText())) {
                if (transaction != null) transaction.apply(statement);
                bindParameters(statement, sqlPlan, context, token);
                token.throwIfCancellationRequested();
                try (ResultSet rs = statement.executeQuery()) {
                    List<ExecutionRow> rows = readRows(rs, sqlPlan, token);
                    ExecutionPageInfo pageInfo = buildPageInfo(rows, sqlPlan, context);
                    long elapsed = Duration.ofNanos(System.nanoTime()-start).toMillis();
                    List<Integer> nodes = sqlPlan.authorization().stream().map(SqlAuthorizationPredicate::nodeId).toList();
                    String fingerprint = ExecutionEvidenceFactory.hash(buildPlanFingerprint(sqlPlan));
                    ExecutionEvidence evidence = ExecutionEvidenceFactory.create("sql", fingerprint, nodes, rows.size(), elapsed, sqlPlan.commandText());
                    return CompletableFuture.completedFuture(new ExecutionResult(rows, pageInfo, evidence, null));
                }
            }
        } catch (Throwable t) { return CompletableFuture.failedFuture(t); }
    }

    private static void bindParameters(PreparedStatement statement, SqlPlan plan, ExecutionContext context, CancellationToken token) throws SQLException {
        int index=1;
        for (SqlParameterBinding binding: plan.effectiveParameters()) {
            token.throwIfCancellationRequested(); Object value=binding.value();
            if (binding.contextPath()!=null) {
                if (!context.effectiveValues().containsKey(binding.contextPath())) throw new IllegalStateException("Execution context does not contain authorization value '"+binding.contextPath()+"'.");
                value=context.effectiveValues().get(binding.contextPath());
                if (ExecutionContextKeys.PAGINATION_LIMIT.equals(binding.contextPath()) && plan.pagination()!=null && value instanceof Number n) value=n.intValue()+1;
            }
            if(value==null) statement.setObject(index,null); else statement.setObject(index,value);
            index++;
        }
    }

    private static List<ExecutionRow> readRows(ResultSet rs, SqlPlan plan, CancellationToken token) throws SQLException {
        List<ExecutionRow> rows=new ArrayList<>();
        Map<String,SqlColumnBinding> bindings=new HashMap<>(); for(SqlColumnBinding b:plan.columns()) bindings.put(b.resultName(),b);
        ResultSetMetaData meta=rs.getMetaData();
        while(rs.next()) { token.throwIfCancellationRequested(); Map<String,Object> values=new LinkedHashMap<>(); Map<ExecutionCellKey,Object> cells=new LinkedHashMap<>();
            for(int i=1;i<=meta.getColumnCount();i++){String name=meta.getColumnLabel(i); if(name==null||name.isEmpty())name=meta.getColumnName(i); Object value=rs.getObject(i); values.put(name,value); SqlColumnBinding b=bindings.get(name); if(b!=null)cells.put(new ExecutionCellKey(b.nodeId(),b.entityId(),b.fieldId()),value);}
            rows.add(new ExecutionRow(values,cells)); }
        return rows;
    }

    private static ExecutionPageInfo buildPageInfo(List<ExecutionRow> rows, SqlPlan plan, ExecutionContext context) {
        if(plan.pagination()==null)return null; var paging=plan.pagination(); int first=paging.first(); Object limit=context.effectiveValues().get(ExecutionContextKeys.PAGINATION_LIMIT); if(limit instanceof Number n)first=n.intValue(); boolean hasCursor=paging.after()!=null; Object cursor=context.effectiveValues().get(ExecutionContextKeys.PAGINATION_HAS_CURSOR); if(cursor instanceof Boolean b)hasCursor=b;
        boolean hasNext=rows.size()>first; if(hasNext)rows.remove(rows.size()-1); if(rows.isEmpty())return new ExecutionPageInfo(null,null,hasNext,hasCursor);
        String start=encodeCursor(rows.get(0),paging.cursorValues()); String end=encodeCursor(rows.get(rows.size()-1),paging.cursorValues()); return new ExecutionPageInfo(start,end,hasNext,hasCursor);
    }
    private static String encodeCursor(ExecutionRow row,List<SqlCursorBinding> bindings){List<Object> values=new ArrayList<>();for(var b:bindings){Object v=row.values().get(b.resultName());if(v==null)throw new IllegalStateException("Cursor ordering fields cannot contain null values.");values.add(v);}return CursorCodec.encode(values);}
    private static String buildPlanFingerprint(SqlPlan p){String cols=p.columns().stream().map(x->x.resultName()+"|"+x.entityId().value()+"|"+x.fieldId().value()+"|"+x.columnName()+"|"+x.nodeId()+"|"+x.isCursor()).sorted().reduce("",(a,b)->a+";"+b);String auth=p.authorization().stream().map(x->x.nodeId()+"|"+x.predicate()).sorted().reduce("",(a,b)->a+";"+b);return p.commandText()+"|"+cols+"|"+auth;}
}
