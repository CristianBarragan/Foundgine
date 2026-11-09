package com.foundgine.providers.storage.sql.mutation;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.*;
import com.foundgine.core.semantic.metadata.IMetadataProvider;

import java.sql.*;
import java.util.*;

/**
 * JDBC execution boundary for provider-neutral SQL mutation plans.
 *
 * <p>The provider owns only the physical SQL execution boundary. Compilation remains a separate
 * concern and is injected when callers want the canonical ExecutionMutationIR -> SQL lowering path.
 */
public class SqlMutationExecutionProvider implements IMutationExecutionProvider {
    private final Connection connection;
    private final boolean ownsTransaction;
    private final SqlMutationCompiler compiler;

    public SqlMutationExecutionProvider(Connection connection) {
        this(connection, null, true);
    }

    public SqlMutationExecutionProvider(Connection connection, boolean ownsTransaction) {
        this(connection, null, ownsTransaction);
    }

    public SqlMutationExecutionProvider(Connection connection, IMetadataProvider metadata) {
        this(connection, metadata, true);
    }

    public SqlMutationExecutionProvider(
            Connection connection, IMetadataProvider metadata, boolean ownsTransaction) {
        this.connection = Objects.requireNonNull(connection);
        this.compiler = metadata == null ? null : new SqlMutationCompiler(metadata);
        this.ownsTransaction = ownsTransaction;
    }

    @Override
    public MutationResult execute(ProviderMutationPlan plan, ExecutionContext context) {
        if (!(plan instanceof SqlMutationPlan p))
            throw new IllegalArgumentException("Expected SqlMutationPlan");
        return executeOne(p, CancellationToken.NONE);
    }

    /**
     * Canonical provider-neutral batch entry point. A compiler must be supplied through the
     * metadata constructor; otherwise callers should compile the IR explicitly before invoking the
     * provider.
     */
    public MutationBatchResult executeBatch(ExecutionMutationIR ir, ExecutionContext context) {
        Objects.requireNonNull(ir, "ir");
        if (compiler == null)
            throw new IllegalStateException(
                    "A metadata-aware SqlMutationCompiler is required to execute"
                            + " ExecutionMutationIR.");
        return executeBatch(compiler.compile(ir), context, CancellationToken.NONE);
    }

    public MutationBatchResult executeBatch(
            ProviderMutationBatchPlan plan, ExecutionContext context, CancellationToken token) {
        Objects.requireNonNull(plan, "plan");
        CancellationToken cancellation = token == null ? CancellationToken.NONE : token;
        cancellation.throwIfCancellationRequested();
        if (!(plan instanceof SqlMutationBatchPlan batch))
            throw new IllegalArgumentException("Expected SqlMutationBatchPlan");

        boolean oldAutoCommit = true;
        try {
            oldAutoCommit = connection.getAutoCommit();
            if (ownsTransaction && oldAutoCommit) connection.setAutoCommit(false);

            List<MutationResult> results = new ArrayList<>(batch.sqlOperations().size());
            for (SqlMutationPlan mutation : batch.sqlOperations()) {
                cancellation.throwIfCancellationRequested();
                results.add(executeOne(mutation, cancellation));
            }

            if (ownsTransaction && oldAutoCommit) connection.commit();
            return new MutationBatchResult(results);
        } catch (Exception e) {
            try {
                if (ownsTransaction && !connection.getAutoCommit()) connection.rollback();
            } catch (SQLException ignored) {
                // Preserve the original execution failure.
            }
            if (e instanceof RuntimeException runtime) throw runtime;
            throw new IllegalStateException("SQL mutation batch execution failed", e);
        } finally {
            try {
                if (ownsTransaction && connection.getAutoCommit() != oldAutoCommit)
                    connection.setAutoCommit(oldAutoCommit);
            } catch (SQLException ignored) {
                // Best effort restoration; the connection owner remains responsible
                // for disposing an unusable connection.
            }
        }
    }

    private MutationResult executeOne(SqlMutationPlan plan, CancellationToken token) {
        token.throwIfCancellationRequested();
        var rewritten =
                com.foundgine.providers.storage.sql.JdbcSqlPlaceholderRewriter.rewriteOrdered(
                        plan.commandText());
        try (PreparedStatement statement = connection.prepareStatement(rewritten.sql())) {
            bind(statement, plan.parameters(), rewritten.placeholderNames());
            AutoCloseable registration =
                    token.register(
                            () -> {
                                try {
                                    statement.cancel();
                                } catch (SQLException ignored) {
                                }
                            });
            try {
                boolean hasResultSet = statement.execute();
                int affected = statement.getUpdateCount();
                if (affected < 0) affected = 0;

                Map<FieldId, Object> values = new LinkedHashMap<>();
                if (!plan.returnedFields().isEmpty()) {
                    if (!hasResultSet) {
                        throw new IllegalStateException(
                                "Mutation requested returned fields but the SQL command did not"
                                        + " produce a result set. PostgreSQL mutations must use"
                                        + " RETURNING rather than JDBC generated-key retrieval.");
                    }
                    // Counterpart of C# ExecuteReader: a returned row counts as one affected row.
                    affected = 0;
                    if (readSingleRow(statement.getResultSet(), plan, values)) affected = 1;
                }

                // Counterpart of C# FallbackCommandText: an upsert whose DO UPDATE guard
                // suppressed the write returns no row, so read the existing row instead.
                if (affected == 0 && values.isEmpty() && plan.fallbackCommandText() != null) {
                    var fallback =
                            com.foundgine.providers.storage.sql.JdbcSqlPlaceholderRewriter
                                    .rewriteOrdered(plan.fallbackCommandText());
                    try (PreparedStatement fallbackStatement =
                            connection.prepareStatement(fallback.sql())) {
                        bind(fallbackStatement, plan.parameters(), fallback.placeholderNames());
                        if (fallbackStatement.execute()
                                && readSingleRow(fallbackStatement.getResultSet(), plan, values)) {
                            affected = 1;
                        }
                    }
                }
                token.throwIfCancellationRequested();
                return new MutationResult(affected, values.isEmpty() ? null : values);
            } finally {
                try {
                    registration.close();
                } catch (Exception ignored) {
                }
            }
        } catch (SQLException e) {
            throw new IllegalStateException("SQL mutation execution failed", e);
        }
    }

    /** Reads at most one row into {@code values}; returns whether a row was present. */
    private static boolean readSingleRow(
            java.sql.ResultSet result, SqlMutationPlan plan, Map<FieldId, Object> values)
            throws SQLException {
        try (result) {
            if (!result.next()) return false;
            for (int i = 0; i < plan.returnedFields().size(); i++) {
                values.put(plan.returnedFields().get(i).fieldId(), result.getObject(i + 1));
            }
            // A mutation returning one row is the canonical contract.
            if (result.next()) {
                throw new IllegalStateException("Mutation returned more than one result row.");
            }
            return true;
        }
    }

    /**
     * Foundgine's SQL writers use deterministic named placeholders (@p0, @p1, ...) because the C#
     * provider binds named DbParameters. JDBC PreparedStatement is positional, so the physical
     * execution boundary normalizes those placeholders to '?'. The parameter list is already
     * emitted in the same ordinal order.
     */
    static String toJdbcSql(String sql) {
        return com.foundgine.providers.storage.sql.JdbcSqlPlaceholderRewriter.rewrite(sql);
    }

    /**
     * Binds one value per JDBC {@code ?}. The SQL may reference a named parameter more than once
     * (an upsert reuses the inserted value in {@code DO UPDATE SET}), so each {@code ?} is resolved
     * by its placeholder name. When a placeholder name has no matching binding the original
     * positional behavior is preserved.
     */
    private static void bind(
            PreparedStatement statement,
            List<com.foundgine.providers.storage.sql.query.SqlParameterBinding> parameters,
            List<String> placeholderNames)
            throws SQLException {
        java.util.Map<String, Object> byName = new java.util.HashMap<>();
        for (var parameter : parameters) byName.put(parameter.name(), parameter.value());

        boolean everyNameKnown =
                !placeholderNames.isEmpty() && byName.keySet().containsAll(placeholderNames);
        if (!everyNameKnown) {
            for (int i = 0; i < parameters.size(); i++)
                statement.setObject(i + 1, parameters.get(i).value());
            return;
        }
        for (int i = 0; i < placeholderNames.size(); i++)
            statement.setObject(i + 1, byName.get(placeholderNames.get(i)));
    }
}
