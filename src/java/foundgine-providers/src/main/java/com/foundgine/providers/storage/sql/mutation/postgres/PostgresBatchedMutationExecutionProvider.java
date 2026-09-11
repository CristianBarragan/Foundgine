package com.foundgine.providers.storage.sql.mutation.postgres;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.*;
import com.foundgine.core.semantic.planning.mutation.MutationBatchPlan;
import com.foundgine.core.semantic.metadata.IMetadataProvider;
import com.foundgine.core.semantic.security.SecurityInvariantIds;
import com.foundgine.providers.storage.sql.mutation.PostgresBatchedMutationCompiler;
import com.foundgine.providers.storage.sql.mutation.SqlBatchedMutationPlan;
import com.foundgine.providers.storage.sql.mutation.SqlMutationBatchPlan;
import com.foundgine.providers.storage.sql.mutation.SqlMutationCompiler;
import com.foundgine.providers.storage.sql.mutation.SqlMutationExecutionProvider;
import com.foundgine.providers.storage.sql.mutation.SqlMutationPlan;

import java.sql.*;
import java.time.*;
import java.util.*;

/**
 * Executes a PostgreSQL mutation batch as one physical statement when the
 * batched compiler can represent it safely. Otherwise the provider falls back
 * to the ordinary sequential SQL mutation provider.
 */
public final class PostgresBatchedMutationExecutionProvider implements IMutationBatchExecutionProvider,
        IMutationSecurityConformanceEvaluator {

    private final Connection connection;
    private final PostgresBatchedMutationCompiler compiler;
    private final SqlMutationCompiler fallbackCompiler;
    private final SqlMutationExecutionProvider fallbackProvider;
    private final ObjectMapper objectMapper;
    private final boolean ownsTransaction;

    public PostgresBatchedMutationExecutionProvider(Connection connection, IMetadataProvider metadata) {
        this(connection, metadata, new ObjectMapper(), true);
    }

    /**
     * Constructs the provider with explicit transaction ownership. When
     * {@code ownsTransaction} is false, the provider participates in the
     * caller's current JDBC transaction and never commits or rolls it back.
     */
    public PostgresBatchedMutationExecutionProvider(Connection connection, IMetadataProvider metadata, boolean ownsTransaction) {
        this(connection, metadata, new ObjectMapper(), ownsTransaction);
    }

    public PostgresBatchedMutationExecutionProvider(Connection connection, IMetadataProvider metadata, ObjectMapper objectMapper) {
        this(connection, metadata, objectMapper, true);
    }

    public PostgresBatchedMutationExecutionProvider(Connection connection, IMetadataProvider metadata,
                                                     ObjectMapper objectMapper, boolean ownsTransaction) {
        this.connection = Objects.requireNonNull(connection, "connection");
        Objects.requireNonNull(metadata, "metadata");
        this.compiler = new PostgresBatchedMutationCompiler(metadata);
        this.fallbackCompiler = new SqlMutationCompiler(metadata);
        this.ownsTransaction = ownsTransaction;
        this.fallbackProvider = new SqlMutationExecutionProvider(connection, metadata, ownsTransaction);
        this.objectMapper = Objects.requireNonNull(objectMapper, "objectMapper");
    }

    @Override
    public MutationSecurityConformanceResult evaluate(ExecutionMutationIR ir) {
        Objects.requireNonNull(ir, "ir");
        if (compiler.tryCompile(ir) == null) fallbackCompiler.compile(ir);
        return new MutationSecurityConformanceResult(
                getClass().getName(),
                List.of(SecurityInvariantIds.PARAMETERIZED_VALUES, SecurityInvariantIds.ATOMIC_MUTATION),
                List.of());
    }

    @Override
    public MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context) {
        return executeBatch(ir, context, CancellationToken.NONE);
    }

    @Override
    public MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context, CancellationToken token) {
        Objects.requireNonNull(ir, "ir");
        Objects.requireNonNull(context, "context");
        CancellationToken cancellation = token == null ? CancellationToken.NONE : token;
        cancellation.throwIfCancellationRequested();
        SqlBatchedMutationPlan batched = compiler.tryCompile(ir);
        if (batched != null) return executeBatchedPlan(batched, cancellation);
        return fallbackProvider.executeBatch(fallbackCompiler.compile(ir), context, cancellation);
    }

    public MutationBatchResult executeBatch(MutationBatchPlan plan, ExecutionContext context) {
        Objects.requireNonNull(plan, "plan");
        Objects.requireNonNull(context, "context");
        SqlBatchedMutationPlan batched = compiler.tryCompile(plan);
        if (batched != null) return executeBatchedPlan(batched, CancellationToken.NONE);
        return fallbackProvider.executeBatch(fallbackCompiler.compile(plan), context, CancellationToken.NONE);
    }

    public MutationBatchResult executeBatch(ProviderMutationBatchPlan plan, ExecutionContext context) {
        return executeBatch(plan, context, CancellationToken.NONE);
    }

    public MutationBatchResult executeBatch(ProviderMutationBatchPlan plan, ExecutionContext context, CancellationToken token) {
        Objects.requireNonNull(plan, "plan");
        Objects.requireNonNull(context, "context");
        CancellationToken cancellation = token == null ? CancellationToken.NONE : token;
        cancellation.throwIfCancellationRequested();
        if (plan instanceof SqlBatchedMutationPlan batched) return executeBatchedPlan(batched, cancellation);
        if (plan instanceof SqlMutationBatchPlan sequential) return fallbackProvider.executeBatch(sequential, context, cancellation);
        throw new IllegalArgumentException("The PostgreSQL batched mutation provider requires a SqlBatchedMutationPlan or SqlMutationBatchPlan.");
    }

    private MutationBatchResult executeBatchedPlan(SqlBatchedMutationPlan plan, CancellationToken cancellation) {
        boolean originalAutoCommit = true;
        boolean startedTransaction = false;
        try {
            if (connection.isClosed()) throw new IllegalStateException("PostgreSQL connection is closed.");
            originalAutoCommit = connection.getAutoCommit();

            // JDBC has no transaction object attached to a command. Transaction
            // ownership is therefore represented by the provider flag plus the
            // connection's auto-commit state. An already-open caller transaction
            // is never committed or rolled back here.
            if (ownsTransaction && originalAutoCommit) {
                connection.setAutoCommit(false);
                startedTransaction = true;
            }

            try (PreparedStatement statement = connection.prepareStatement(toJdbcSql(plan.commandText()))) {
                bind(statement, plan.parameters());
                cancellation.throwIfCancellationRequested();

                AutoCloseable registration = cancellation.register(() -> {
                    try { statement.cancel(); } catch (SQLException ignored) { }
                });
                try {
                    boolean hasResultSet = statement.execute();
                    if (!hasResultSet) {
                        throw new IllegalStateException("PostgreSQL batched mutation did not produce its result set.");
                    }

                    Map<Integer, SqlBatchedMutationPlan.GroupMeta> groups = new HashMap<>();
                    for (SqlBatchedMutationPlan.GroupMeta group : plan.groups()) groups.put(group.groupId(), group);
                    Map<String, SqlBatchedMutationPlan.RowKey> rowKeys = new HashMap<>();
                    for (SqlBatchedMutationPlan.RowKey key : plan.rowKeys()) rowKeys.put(key.groupId() + ":" + key.ordinal(), key);

                    MutationResult[] results = new MutationResult[plan.operationCount()];
                    boolean[] seen = new boolean[plan.operationCount()];
                    @SuppressWarnings("unchecked") Map<FieldId, Object>[] returned = new Map[plan.operationCount()];
                    int[] affected = new int[plan.operationCount()];

                    try (ResultSet reader = statement.getResultSet()) {
                        while (reader.next()) {
                            cancellation.throwIfCancellationRequested();
                            int groupId = reader.getInt(1);
                            String json = reader.getString(2);
                            SqlBatchedMutationPlan.GroupMeta group = groups.get(groupId);
                            if (group == null) throw new IllegalStateException("PostgreSQL batched mutation returned unknown group '" + groupId + "'.");

                            JsonNode root;
                            try { root = objectMapper.readTree(json); }
                            catch (Exception ex) { throw new IllegalStateException("Invalid PostgreSQL batched mutation JSON result.", ex); }

                            int operationIndex;
                            if (group.ordinalAddressable()) {
                                JsonNode ordinalNode = root.get("__fg_corr");
                                if (ordinalNode == null || !ordinalNode.canConvertToInt())
                                    throw new IllegalStateException("Batched mutation group '" + groupId + "' did not return a valid __fg_corr correlation value.");
                                int ordinal = ordinalNode.intValue();
                                if (ordinal < 1 || ordinal > group.operationIndexes().size())
                                    throw new IllegalStateException("Batched mutation group '" + groupId + "' returned invalid ordinal '" + ordinal + "'.");
                                operationIndex = group.operationIndexes().get(ordinal - 1);
                                if (!rowKeys.containsKey(groupId + ":" + ordinal))
                                    throw new IllegalStateException("Batched mutation returned an unmapped row ordinal '" + ordinal + "' for group '" + groupId + "'.");
                            } else {
                                operationIndex = group.operationIndexes().get(0);
                                if (seen[operationIndex]) continue;
                            }

                            seen[operationIndex] = true;
                            affected[operationIndex] = 1;
                            Map<FieldId, Object> values = new LinkedHashMap<>();
                            Iterator<Map.Entry<String, JsonNode>> fields = root.fields();
                            while (fields.hasNext()) {
                                Map.Entry<String, JsonNode> property = fields.next();
                                String name = property.getKey();
                                if (name.equals("__fg_corr") || name.equals("__affected") || name.startsWith("r__k_")) continue;
                                if (!name.startsWith("r_")) continue;
                                long id;
                                try { id = Long.parseUnsignedLong(name.substring(2)); }
                                catch (RuntimeException ex) { continue; }
                                FieldId fieldId = new FieldId(id);
                                Class<?> clrType = group.returnedFieldTypes().get(fieldId);
                                if (clrType == null) continue;
                                values.put(fieldId, convertJsonValue(property.getValue(), clrType));
                            }
                            returned[operationIndex] = values.isEmpty() ? null : values;
                        }
                    }

                    cancellation.throwIfCancellationRequested();
                    for (int i = 0; i < plan.operationCount(); i++) results[i] = new MutationResult(affected[i], returned[i]);

                    for (SqlBatchedMutationPlan.GroupMeta group : plan.groups()) {
                        if (!group.ordinalAddressable()) continue;
                        for (int operationIndex : group.operationIndexes()) {
                            if (!seen[operationIndex])
                                throw new IllegalStateException("PostgreSQL batched mutation produced no result for operation " + operationIndex
                                        + ". This usually means two operations collapsed onto the same conflict/correlation key.");
                        }
                    }

                    if (startedTransaction) connection.commit();
                    return new MutationBatchResult(Arrays.asList(results));
                } finally {
                    try { registration.close(); } catch (Exception ignored) { }
                }
            }
        } catch (RuntimeException | SQLException ex) {
            if (startedTransaction) {
                try { connection.rollback(); } catch (SQLException ignored) { }
            }
            if (ex instanceof RuntimeException runtime) throw runtime;
            throw new IllegalStateException("PostgreSQL batched mutation execution failed", ex);
        } finally {
            if (startedTransaction) {
                try { connection.setAutoCommit(originalAutoCommit); } catch (SQLException ignored) { }
            }
        }
    }

    private void bind(PreparedStatement statement, List<com.foundgine.providers.storage.sql.query.SqlParameterBinding> parameters) throws SQLException {
        for (int i = 0; i < parameters.size(); i++) {
            Object value = parameters.get(i).value();
            if (value != null && value.getClass().isArray() && !(value instanceof byte[])) {
                Array sqlArray = createSqlArray(value);
                statement.setArray(i + 1, sqlArray);
            } else {
                statement.setObject(i + 1, value);
            }
        }
    }

    private Array createSqlArray(Object array) throws SQLException {
        Class<?> component = array.getClass().getComponentType();
        String sqlType = postgresArrayElementType(component);
        int length = java.lang.reflect.Array.getLength(array);
        Object[] values = new Object[length];
        for (int i = 0; i < length; i++) values[i] = java.lang.reflect.Array.get(array, i);
        return connection.createArrayOf(sqlType, values);
    }

    private static String postgresArrayElementType(Class<?> type) {
        if (type == String.class || type == Character.class || type == char.class) return "text";
        if (type == Integer.class || type == int.class) return "integer";
        if (type == Long.class || type == long.class) return "bigint";
        if (type == Short.class || type == short.class) return "smallint";
        if (type == Byte.class || type == byte.class) return "smallint";
        if (type == Boolean.class || type == boolean.class) return "boolean";
        if (type == Float.class || type == float.class) return "real";
        if (type == Double.class || type == double.class) return "double precision";
        if (type == java.math.BigDecimal.class) return "numeric";
        if (type == UUID.class) return "uuid";
        if (type == LocalDate.class) return "date";
        if (type == LocalDateTime.class) return "timestamp";
        if (type == OffsetDateTime.class) return "timestamptz";
        if (type == Instant.class) return "timestamptz";
        return "text";
    }

    private Object convertJsonValue(JsonNode node, Class<?> targetType) {
        if (node == null || node.isNull()) return null;
        Class<?> target = wrap(targetType);
        try {
            return objectMapper.treeToValue(node, target);
        } catch (Exception ignored) {
            if (target == UUID.class && node.isTextual()) return UUID.fromString(node.textValue());
            if (target == LocalDate.class && node.isTextual()) return LocalDate.parse(node.textValue());
            if (target == LocalDateTime.class && node.isTextual()) return LocalDateTime.parse(node.textValue());
            if (target == OffsetDateTime.class && node.isTextual()) return OffsetDateTime.parse(node.textValue());
            if (target == Instant.class && node.isTextual()) return Instant.parse(node.textValue());
            if (target == java.math.BigDecimal.class && node.isNumber()) return node.decimalValue();
            if (node.isTextual()) return node.textValue();
            if (node.isIntegralNumber()) return node.longValue();
            if (node.isFloatingPointNumber()) return node.doubleValue();
            if (node.isBoolean()) return node.booleanValue();
            return node.toString();
        }
    }

    private static Class<?> wrap(Class<?> type) {
        if (!type.isPrimitive()) return type;
        if (type == int.class) return Integer.class;
        if (type == long.class) return Long.class;
        if (type == boolean.class) return Boolean.class;
        if (type == short.class) return Short.class;
        if (type == byte.class) return Byte.class;
        if (type == float.class) return Float.class;
        if (type == double.class) return Double.class;
        if (type == char.class) return Character.class;
        return type;
    }

    private static String toJdbcSql(String sql) {
        return com.foundgine.providers.storage.sql.JdbcSqlPlaceholderRewriter.rewrite(sql);
    }
}
