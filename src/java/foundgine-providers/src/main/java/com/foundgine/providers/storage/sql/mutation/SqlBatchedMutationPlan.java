package com.foundgine.providers.storage.sql.mutation;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.mutation.ProviderMutationBatchPlan;
import com.foundgine.core.semantic.planning.mutation.MutationDependency;
import com.foundgine.providers.storage.sql.query.SqlParameterBinding;

import java.util.*;

/** Provider-specific PostgreSQL mutation plan containing one physical SQL command for a logical batch. */
public final class SqlBatchedMutationPlan extends ProviderMutationBatchPlan {
    public record RowKey(int groupId, int ordinal) {}

    public record GroupMeta(int groupId, boolean ordinalAddressable, List<Integer> operationIndexes,
                            Map<FieldId, Class<?>> returnedFieldTypes) {
        public GroupMeta {
            operationIndexes = List.copyOf(operationIndexes);
            returnedFieldTypes = Map.copyOf(returnedFieldTypes);
        }
    }

    private final String commandText;
    private final List<SqlParameterBinding> parameters;
    private final List<GroupMeta> groups;
    private final List<RowKey> rowKeys;
    private final int operationCount;
    private final List<MutationDependency> dependencies;

    public SqlBatchedMutationPlan(String sql, List<SqlParameterBinding> parameters, List<GroupMeta> groups,
                                  List<RowKey> rowKeys, int operationCount, List<SqlMutationPlan> operations,
                                  List<MutationDependency> dependencies) {
        super(new ArrayList<>(operations));
        this.commandText = Objects.requireNonNull(sql);
        this.parameters = List.copyOf(parameters);
        this.groups = List.copyOf(groups);
        this.rowKeys = List.copyOf(rowKeys);
        this.operationCount = operationCount;
        this.dependencies = List.copyOf(dependencies);
    }

    /** Compatibility constructor for callers that do not need dependency metadata. */
    public SqlBatchedMutationPlan(String sql, List<SqlParameterBinding> parameters, List<GroupMeta> groups,
                                  List<RowKey> rowKeys, int operationCount, List<SqlMutationPlan> operations) {
        this(sql, parameters, groups, rowKeys, operationCount, operations, List.of());
    }

    public String commandText() { return commandText; }
    public List<SqlParameterBinding> parameters() { return parameters; }
    public List<GroupMeta> groups() { return groups; }
    public List<RowKey> rowKeys() { return rowKeys; }
    public int operationCount() { return operationCount; }
    public List<MutationDependency> dependencies() { return dependencies; }
}
