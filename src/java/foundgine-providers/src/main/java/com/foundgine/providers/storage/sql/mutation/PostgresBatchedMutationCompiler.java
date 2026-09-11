package com.foundgine.providers.storage.sql.mutation;

import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.mutation.ExecutionMutationIR;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.IMetadataProvider;
import com.foundgine.core.semantic.planning.mutation.MutationBatchPlan;
import com.foundgine.core.semantic.planning.mutation.MutationDependency;
import com.foundgine.core.semantic.planning.mutation.MutationFieldValue;
import com.foundgine.core.semantic.planning.mutation.MutationKind;
import com.foundgine.core.semantic.planning.mutation.MutationOperation;
import com.foundgine.core.semantic.planning.mutation.MutationValueReference;
import com.foundgine.core.semantic.planning.mutation.MutationPlan;
import com.foundgine.providers.storage.sql.query.SqlParameterBinding;

import java.lang.reflect.Array;
import java.util.*;
import java.util.regex.Matcher;

/**
 * Compiles an entire mutation batch into one PostgreSQL statement.
 *
 * <p>Create/Upsert operations at the same dependency level are grouped by shape
 * and expanded with PostgreSQL {@code unnest(array parameters)}. Reference-valued
 * fields use the source group's 1-based ordinal and an ord-map CTE, so generated
 * values flow between levels without client-side round trips.
 *
 * <p>Update/Delete operations remain one CTE each, but are folded into the same
 * physical statement. Unsupported shapes are rejected by {@link #tryCompile};
 * callers can then use the sequential SQL mutation compiler.
 */
public final class PostgresBatchedMutationCompiler {
    private final IMetadataProvider metadata;

    public PostgresBatchedMutationCompiler(IMetadataProvider metadata) {
        this.metadata = Objects.requireNonNull(metadata, "metadata");
    }

    /** Canonical provider entry point. */
    public SqlBatchedMutationPlan compile(ExecutionMutationIR ir) {
        Objects.requireNonNull(ir, "ir");
        ir.validateDerivedDependencies();
        return compile(new MutationBatchPlan(ir.operations(), ir.deriveDependencies()));
    }

    public SqlBatchedMutationPlan compile(MutationBatchPlan plan) {
        Objects.requireNonNull(plan, "plan");
        if (plan.operations().isEmpty())
            throw new IllegalStateException("A mutation batch must contain at least one operation.");

        List<MutationOperation> ops = plan.operations();
        int[] levels = computeLevels(ops.size(), plan.dependencies());
        int[] opToGroup = new int[ops.size()];
        Arrays.fill(opToGroup, -1);
        List<OpGroup> groups = new ArrayList<>();
        Map<Integer, LinkedHashSet<FieldId>> forcedReturns = new HashMap<>();

        TreeSet<Integer> distinctLevels = new TreeSet<>();
        for (int level : levels) distinctLevels.add(level);

        for (int level : distinctLevels) {
            List<Integer> indexes = new ArrayList<>();
            for (int i = 0; i < ops.size(); i++) if (levels[i] == level) indexes.add(i);

            Map<String, OpGroup> byShape = new LinkedHashMap<>();
            for (int opIndex : indexes) {
                MutationOperation op = ops.get(opIndex);
                if (op.kind() == MutationKind.CREATE || op.kind() == MutationKind.UPSERT) {
                    String shape = shapeKey(op, opToGroup);
                    OpGroup group = byShape.get(shape);
                    if (group == null) {
                        group = new OpGroup(groups.size(), op.entity().id(), op.kind(), op);
                        byShape.put(shape, group);
                        groups.add(group);
                    }
                    group.operationIndexes.add(opIndex);
                    opToGroup[opIndex] = group.groupId;
                } else {
                    OpGroup group = new OpGroup(groups.size(), op.entity().id(), op.kind(), op);
                    group.operationIndexes.add(opIndex);
                    groups.add(group);
                    opToGroup[opIndex] = group.groupId;
                }
            }

            for (int opIndex : indexes) {
                for (MutationFieldValue field : ops.get(opIndex).fields()) {
                    if (field.source() == null) continue;
                    int sourceGroup = opToGroup[field.source().sourceOperationIndex()];
                    forcedReturns.computeIfAbsent(sourceGroup, ignored -> new LinkedHashSet<>())
                            .add(field.source().sourceField());
                }
            }
        }

        validateCorrelation(ops, plan.dependencies(), levels, groups, opToGroup);

        for (MutationDependency dependency : plan.dependencies()) {
            if (dependency.sourceOperationIndex() < 0 || dependency.sourceOperationIndex() >= ops.size()
                    || dependency.targetOperationIndex() < 0 || dependency.targetOperationIndex() >= ops.size()
                    || levels[dependency.targetOperationIndex()] <= levels[dependency.sourceOperationIndex()]) {
                throw new IllegalStateException("Invalid mutation dependency "
                        + dependency.sourceOperationIndex() + " -> " + dependency.targetOperationIndex() + ".");
            }
        }

        StringBuilder sql = new StringBuilder("WITH ");
        List<SqlParameterBinding> parameters = new ArrayList<>();
        List<GroupOutputMeta> output = new ArrayList<>();

        for (int i = 0; i < groups.size(); i++) {
            if (i > 0) sql.append(",\n");
            OpGroup group = groups.get(i);
            Set<FieldId> forced = forcedReturns.getOrDefault(group.groupId, new LinkedHashSet<>());
            GroupOutputMeta meta;
            if (group.kind == MutationKind.CREATE)
                meta = writeCreateOrUpsertGroup(sql, parameters, group, ops, output, forced, false);
            else if (group.kind == MutationKind.UPSERT)
                meta = writeCreateOrUpsertGroup(sql, parameters, group, ops, output, forced, true);
            else if (group.kind == MutationKind.UPDATE)
                meta = writeUpdateOrDeleteGroup(sql, parameters, group, ops.get(group.operationIndexes.get(0)), forced, false);
            else if (group.kind == MutationKind.DELETE)
                meta = writeUpdateOrDeleteGroup(sql, parameters, group, ops.get(group.operationIndexes.get(0)), forced, true);
            else
                throw new UnsupportedOperationException("Unsupported mutation kind '" + group.kind + "'.");
            output.add(meta);
        }

        sql.append("\nSELECT * FROM (\n");
        for (int i = 0; i < output.size(); i++) {
            if (i > 0) sql.append("\nUNION ALL\n");
            GroupOutputMeta meta = output.get(i);
            sql.append("  SELECT ").append(i).append(" AS __grp, ");
            if (meta.ordinalAddressable) {
                sql.append("jsonb_build_object('__fg_corr', f.__fg_corr) || (to_jsonb(f) - '__fg_corr')");
            } else {
                sql.append("to_jsonb(f)");
            }
            sql.append(" AS __row FROM ")
                    .append(meta.ordinalAddressable && meta.ordMapCteName != null ? meta.ordMapCteName : meta.resultCteName)
                    .append(" f");
        }
        sql.append("\n) __all ORDER BY __grp, CASE WHEN __row ? '__fg_corr' THEN ((__row ->> '__fg_corr')::bigint) END");

        List<SqlBatchedMutationPlan.RowKey> rowKeys = new ArrayList<>(ops.size());
        for (int opIndex = 0; opIndex < ops.size(); opIndex++) {
            OpGroup group = groups.get(opToGroup[opIndex]);
            rowKeys.add(new SqlBatchedMutationPlan.RowKey(group.groupId, groupOrdinal(group.operationIndexes, opIndex)));
        }

        List<SqlBatchedMutationPlan.GroupMeta> groupMetas = new ArrayList<>();
        for (GroupOutputMeta meta : output) {
            groupMetas.add(new SqlBatchedMutationPlan.GroupMeta(
                    meta.groupId, meta.ordinalAddressable, meta.operationIndexes, meta.returnedFieldTypes));
        }

        return new SqlBatchedMutationPlan(sql.toString(), parameters, groupMetas, rowKeys, ops.size(),
                dummyOperations(ops.size()), plan.dependencies());
    }

    /** Safe compilation path; {@code null} means use the sequential provider. */
    public SqlBatchedMutationPlan tryCompile(ExecutionMutationIR ir) {
        Objects.requireNonNull(ir, "ir");
        return tryCompile(ir.toMutationBatchPlan());
    }

    public SqlBatchedMutationPlan tryCompile(MutationBatchPlan plan) {
        Objects.requireNonNull(plan, "plan");
        if (requiresSequentialEntityMutation(plan.operations())) return null;
        try {
            return compile(plan);
        } catch (UnsupportedOperationException ex) {
            return null;
        }
    }

    private static boolean requiresSequentialEntityMutation(List<MutationOperation> operations) {
        Map<EntityId, EnumSet<MutationKind>> kinds = new HashMap<>();
        for (MutationOperation operation : operations)
            kinds.computeIfAbsent(operation.entity().id(), ignored -> EnumSet.noneOf(MutationKind.class)).add(operation.kind());
        for (EnumSet<MutationKind> set : kinds.values()) {
            boolean insertLike = set.contains(MutationKind.CREATE) || set.contains(MutationKind.UPSERT);
            boolean updateLike = set.contains(MutationKind.UPDATE) || set.contains(MutationKind.DELETE);
            if (insertLike && updateLike) return true;
        }
        return false;
    }

    private void validateCorrelation(List<MutationOperation> operations, List<MutationDependency> dependencies,
                                     int[] levels, List<OpGroup> groups, int[] opToGroup) {
        Set<String> dependencySet = new HashSet<>();
        for (MutationDependency d : dependencies)
            dependencySet.add(dependencyKey(d.sourceOperationIndex(), d.targetOperationIndex(), d.sourceField(), d.targetColumn()));

        for (int targetIndex = 0; targetIndex < operations.size(); targetIndex++) {
            MutationOperation target = operations.get(targetIndex);
            for (MutationFieldValue field : target.fields()) {
                var source = field.source();
                if (source == null) continue;
                int sourceIndex = source.sourceOperationIndex();
                if (sourceIndex < 0 || sourceIndex >= operations.size())
                    throw new IllegalStateException("Mutation operation " + targetIndex + " references missing source operation " + sourceIndex + ".");
                if (sourceIndex == targetIndex)
                    throw new IllegalStateException("Mutation operation " + targetIndex + " cannot reference itself.");
                if (levels[sourceIndex] >= levels[targetIndex])
                    throw new IllegalStateException("Mutation reference " + sourceIndex + " -> " + targetIndex + " does not point from an earlier dependency level.");

                MutationOperation sourceOperation = operations.get(sourceIndex);
                if (sourceOperation.kind() == MutationKind.DELETE)
                    throw new UnsupportedOperationException("Mutation operation " + targetIndex + " references a deleted source operation " + sourceIndex + "; generated-key correlation cannot use DELETE.");

                FieldMetadata sourceField = findField(metadata.getEntity(sourceOperation.entity().id()), source.sourceField());
                if (sourceField.column() == null)
                    throw new IllegalStateException("Source field " + source.sourceField().value() + " has no physical column and cannot participate in provider correlation.");

                String dep = dependencyKey(sourceIndex, targetIndex, source.sourceField(), field.column());
                if (!dependencySet.contains(dep))
                    throw new IllegalStateException("Mutation value reference " + sourceIndex + ":" + source.sourceField().value()
                            + " for target operation " + targetIndex + ":" + field.column().value() + " has no matching dependency edge.");

                OpGroup sourceGroup = groups.get(opToGroup[sourceIndex]);
                if (!sourceGroup.operationIndexes.contains(sourceIndex))
                    throw new IllegalStateException("Source operation " + sourceIndex + " is not present in its physical group.");
                if (sourceGroup.kind == MutationKind.DELETE)
                    throw new UnsupportedOperationException("Source operation " + sourceIndex + " is a DELETE group and cannot provide an ordinal correlation map.");
            }
        }
    }

    private GroupOutputMeta writeCreateOrUpsertGroup(StringBuilder sql, List<SqlParameterBinding> parameters,
                                                      OpGroup group, List<MutationOperation> operations,
                                                      List<GroupOutputMeta> priorGroups, Set<FieldId> forcedReturns,
                                                      boolean isUpsert) {
        EntityMetadata entity = metadata.getEntity(group.entity);
        List<MutationOperation> rows = new ArrayList<>();
        for (int index : group.operationIndexes) rows.add(operations.get(index));
        List<ColumnId> columns = new ArrayList<>();
        for (MutationFieldValue f : group.template.fields()) columns.add(f.column());
        if (columns.isEmpty()) throw new IllegalStateException("Mutation group " + group.groupId + " has no fields.");

        List<ColumnId> conflicts;
        if (isUpsert) {
            conflicts = group.template.conflictColumns() != null
                    ? new ArrayList<>(group.template.conflictColumns())
                    : entity.primaryKey() != null ? List.of(entity.primaryKey().columnId()) : List.of();
        } else {
            conflicts = new ArrayList<>();
            for (ColumnId c : columns) {
                boolean allLiteral = true;
                for (MutationOperation r : rows) {
                    if (fieldForColumn(r, c).source() != null) { allLiteral = false; break; }
                }
                if (allLiteral) conflicts.add(c);
            }
        }

        if (isUpsert && conflicts.isEmpty()) throw new IllegalStateException("Upsert '" + entity.name() + "' has no conflict identity.");
        if (isUpsert) {
            for (ColumnId c : conflicts) if (!columns.contains(c))
                throw new UnsupportedOperationException("Upsert '" + entity.name() + "' does not supply every conflict column; falling back to sequential execution.");
            for (MutationOperation r : rows) for (ColumnId c : conflicts) {
                MutationFieldValue f = fieldForColumn(r, c);
                if (f.source() == null && f.value() == null)
                    throw new UnsupportedOperationException("Upsert '" + entity.name() + "' contains a NULL literal conflict value; falling back to sequential execution.");
            }
            if (rows.size() > 1 && conflicts.stream().allMatch(c -> rows.stream().allMatch(r -> fieldForColumn(r, c).source() == null))
                    && hasDuplicateLiteralKey(rows, conflicts))
                throw new UnsupportedOperationException("Upsert '" + entity.name() + "' contains duplicate literal conflict keys; falling back to sequential execution.");
        }

        List<String> columnNames = columns.stream().map(c -> resolveColumn(entity, c)).toList();
        List<String> conflictNames = conflicts.stream().map(c -> resolveColumn(entity, c)).toList();
        String src = "g" + group.groupId + "_src";
        String keys = "g" + group.groupId + "_keys";
        String ins = "g" + group.groupId + "_created";
        String fin = "g" + group.groupId + "_final";
        String ordmap = "g" + group.groupId + "_ordmap";

        List<String> sourceExpressions = new ArrayList<>();
        List<String> sourceAliases = new ArrayList<>();
        for (ColumnId column : columns) {
            MutationFieldValue templateField = fieldForColumn(rows.get(0), column);
            String name = resolveColumn(entity, column);
            if (templateField.source() == null) {
                Object[] values = new Object[rows.size()];
                for (int i = 0; i < rows.size(); i++) values[i] = fieldForColumn(rows.get(i), column).value();
                String parameterName = bindTypedArrayParameter(parameters, "g" + group.groupId + "_" + name, values);
                sourceExpressions.add("@" + parameterName);
                sourceAliases.add(name);
            } else {
                Object[] ordinals = new Object[rows.size()];
                for (int i = 0; i < rows.size(); i++) {
                    MutationValueReference source = fieldForColumn(rows.get(i), column).source();
                    GroupOutputMeta sourceGroup = findPriorGroup(priorGroups, source.sourceOperationIndex());
                    if (sourceGroup == null || !sourceGroup.ordinalAddressable)
                        throw new UnsupportedOperationException("Mutation operation " + source.sourceOperationIndex() + " cannot be used as a batched reference source.");
                    ordinals[i] = groupOrdinal(sourceGroup.operationIndexes, source.sourceOperationIndex());
                }
                String parameterName = bindTypedArrayParameter(parameters, "g" + group.groupId + "_" + name + "_ord", ordinals);
                sourceExpressions.add("@" + parameterName);
                sourceAliases.add(name + "__fg_corr");
            }
        }

        sql.append(src).append(" AS (\n  SELECT * FROM unnest(")
                .append(String.join(", ", sourceExpressions)).append(") WITH ORDINALITY AS s(")
                .append(String.join(", ", sourceAliases.stream().map(PostgresBatchedMutationCompiler::q).toList()))
                .append(", __fg_corr)\n)");

        String resolved = "g" + group.groupId + "_resolved";
        sql.append(",\n").append(resolved).append(" AS (\n  SELECT s.__fg_corr");
        for (ColumnId column : columns) {
            MutationFieldValue field = fieldForColumn(rows.get(0), column);
            String name = resolveColumn(entity, column);
            sql.append(", ");
            if (field.source() == null) {
                sql.append("s.").append(q(name)).append(" AS ").append(q(name));
            } else {
                GroupOutputMeta sourceGroup = findPriorGroup(priorGroups, field.source().sourceOperationIndex());
                if (sourceGroup == null) throw new UnsupportedOperationException("Source operation " + field.source().sourceOperationIndex() + " is not available to resolve references.");
                String sourceField = sourceGroup.returnedFieldNames.get(field.source().sourceField());
                if (sourceField == null)
                    throw new UnsupportedOperationException("Source operation " + field.source().sourceOperationIndex() + " does not return field " + field.source().sourceField().value() + ".");
                String alias = "p" + column.value();
                sql.append(alias).append(".").append(q(sourceField)).append(" AS ").append(q(name));
            }
        }
        sql.append("\n  FROM ").append(src).append(" s");

        List<ColumnId> referenceColumns = new ArrayList<>();
        for (ColumnId c : columns) if (fieldForColumn(rows.get(0), c).source() != null) referenceColumns.add(c);
        for (ColumnId column : referenceColumns) {
            MutationFieldValue field = fieldForColumn(rows.get(0), column);
            GroupOutputMeta source = Objects.requireNonNull(findPriorGroup(priorGroups, field.source().sourceOperationIndex()));
            String alias = "p" + column.value();
            sql.append("\n  JOIN ").append(source.ordMapCteName).append(" ").append(alias)
                    .append(" ON ").append(alias).append(".__fg_corr = s.")
                    .append(q(resolveColumn(entity, column) + "__fg_corr"));
        }
        sql.append("\n)");

        String input = "g" + group.groupId + "_input";
        if (isUpsert) {
            sql.append(",\n").append(input).append(" AS (\n  SELECT DISTINCT ON (")
                    .append(String.join(", ", conflictNames.stream().map(PostgresBatchedMutationCompiler::q).toList()))
                    .append(") * FROM ").append(resolved).append("\n  ORDER BY ")
                    .append(String.join(", ", conflictNames.stream().map(PostgresBatchedMutationCompiler::q).toList()))
                    .append(", __fg_corr DESC\n)");
            sql.append(",\n").append(keys).append(" AS (\n  SELECT __fg_corr, ")
                    .append(String.join(", ", conflictNames.stream().map(PostgresBatchedMutationCompiler::q).toList()))
                    .append("\n  FROM ").append(input).append("\n)");
        } else {
            sql.append(",\n").append(input).append(" AS (SELECT * FROM ").append(resolved).append(")");
        }

        List<FieldId> requestedFields = requestedFields(group.template, entity, forcedReturns);
        List<ReturnColumn> returnColumns = buildReturnColumns(entity, requestedFields, conflicts);
        Map<FieldId, Class<?>> returnedTypes = buildReturnedTypes(entity, requestedFields);
        Map<FieldId, String> returnNames = new LinkedHashMap<>();
        for (FieldId id : requestedFields) returnNames.put(id, "r_" + id.value());

        sql.append(",\n").append(ins).append(" AS (\n");
        if (!isUpsert) {
            sql.append("  MERGE INTO ").append(table(entity.effectiveStorageName())).append(" t\n")
                    .append("  USING ").append(input).append(" r ON FALSE\n")
                    .append("  WHEN NOT MATCHED THEN INSERT (").append(String.join(", ", columnNames.stream().map(PostgresBatchedMutationCompiler::q).toList())).append(") VALUES (")
                    .append(String.join(", ", columnNames.stream().map(c -> "r." + q(c)).toList())).append(")\n")
                    .append("  RETURNING r.__fg_corr, ")
                    .append(String.join(", ", returnColumns.stream().map(x -> "t." + q(x.columnName) + " AS " + q(x.alias)).toList()));
        } else {
            sql.append("  INSERT INTO ").append(table(entity.effectiveStorageName()))
                    .append(" (").append(String.join(", ", columnNames.stream().map(PostgresBatchedMutationCompiler::q).toList())).append(")\n  SELECT ")
                    .append(String.join(", ", columnNames.stream().map(c -> "r." + q(c)).toList()))
                    .append("\n  FROM ").append(input).append(" r\n  ON CONFLICT (")
                    .append(String.join(", ", conflictNames.stream().map(PostgresBatchedMutationCompiler::q).toList())).append(") ");
            List<String> updateColumns = new ArrayList<>();
            for (int i = 0; i < columnNames.size(); i++) if (!conflicts.contains(columns.get(i))) updateColumns.add(columnNames.get(i));
            if (updateColumns.isEmpty()) sql.append("DO NOTHING");
            else sql.append("DO UPDATE SET ").append(String.join(", ", updateColumns.stream().map(c -> q(c) + " = EXCLUDED." + q(c)).toList()))
                    .append("\n  WHERE ").append(String.join(" OR ", updateColumns.stream().map(c -> table(entity.effectiveStorageName()) + "." + q(c) + " IS DISTINCT FROM EXCLUDED." + q(c)).toList()));
        }
        if (isUpsert) {
            sql.append("\n  RETURNING ").append(String.join(", ", returnColumns.stream().map(x -> q(x.columnName) + " AS " + q(x.alias)).toList())).append("\n)");
        } else {
            sql.append("\n)");
        }

        if (isUpsert) {
            sql.append(",\n").append(fin).append(" AS (\n  SELECT * FROM ").append(ins)
                    .append("\n  UNION ALL\n  SELECT ").append(String.join(", ", returnColumns.stream().map(x -> "t." + q(x.columnName) + " AS " + q(x.alias)).toList()))
                    .append("\n  FROM ").append(table(entity.effectiveStorageName())).append(" t\n  JOIN ").append(keys).append(" k ON ")
                    .append(String.join(" AND ", conflictNames.stream().map(c -> "t." + q(c) + " IS NOT DISTINCT FROM k." + q(c)).toList()))
                    .append("\n  WHERE NOT EXISTS (SELECT 1 FROM ").append(ins).append(" i WHERE ")
                    .append(String.join(" AND ", conflictNames.stream().map(c -> "i." + q(returnAliasForColumn(returnColumns, c)) + " IS NOT DISTINCT FROM k." + q(c)).toList()))
                    .append(")\n)");
            sql.append(",\n").append(ordmap).append(" AS (\n  SELECT k.__fg_corr, f.*\n  FROM ").append(keys).append(" k\n  JOIN ").append(fin).append(" f ON ")
                    .append(String.join(" AND ", conflictNames.stream().map(c -> "k." + q(c) + " IS NOT DISTINCT FROM f." + q(returnAliasForColumn(returnColumns, c))).toList())).append("\n)");
        } else {
            sql.append(",\n").append(fin).append(" AS (SELECT * FROM ").append(ins).append(")")
                    .append(",\n").append(ordmap).append(" AS (SELECT * FROM ").append(fin).append(")");
        }

        return new GroupOutputMeta(group.groupId, fin, ordmap, group.operationIndexes, true, false, returnNames, returnedTypes);
    }

    private GroupOutputMeta writeUpdateOrDeleteGroup(StringBuilder sql, List<SqlParameterBinding> parameters,
                                                       OpGroup group, MutationOperation operation,
                                                       Set<FieldId> forcedReturns, boolean isDelete) {
        EntityMetadata entity = metadata.getEntity(group.entity);
        List<FieldId> returnFields = new ArrayList<>();
        if (operation.returnFields() != null && !operation.returnFields().isEmpty()) returnFields.addAll(operation.returnFields());
        else if (!isDelete) for (FieldMetadata f : entity.effectiveFields()) if (f.column() != null) returnFields.add(f.id());
        for (FieldId f : forcedReturns) if (!returnFields.contains(f)) returnFields.add(f);
        if (isDelete && !forcedReturns.isEmpty()) throw new UnsupportedOperationException("A Delete cannot currently be a source of a batched reference; falling back to sequential execution.");

        MutationOperation effective = new MutationOperation(operation.entity(), operation.kind(), operation.fields(), operation.filter(), operation.conflictColumns(),
                returnFields.isEmpty() && !isDelete ? null : returnFields);
        SqlMutationPlan single = new SqlMutationCompiler(metadata).compile(new MutationPlan(List.of(effective)));
        int offset = parameters.size();
        String rewritten = java.util.regex.Pattern.compile("@p([0-9]+)").matcher(single.commandText())
                .replaceAll(m -> Matcher.quoteReplacement("@p" + (offset + Integer.parseInt(m.group(1)))));
        if (isDelete && !rewritten.toLowerCase(Locale.ROOT).contains(" returning ")) rewritten += " RETURNING 1 AS \"__affected\"";
        sql.append("g").append(group.groupId).append("_op AS (\n  ").append(rewritten).append("\n)");
        for (SqlParameterBinding p : single.parameters()) {
            int old = Integer.parseInt(p.name().substring(1));
            parameters.add(new SqlParameterBinding("p" + (offset + old), p.value(), p.source(), p.contextPath(), p.clrType()));
        }

        Map<FieldId, String> resultNames = new LinkedHashMap<>();
        Map<FieldId, Class<?>> resultTypes = new LinkedHashMap<>();
        for (FieldId fieldId : returnFields) {
            FieldMetadata field = findField(entity, fieldId);
            resultNames.put(fieldId, "r_" + fieldId.value());
            resultTypes.put(fieldId, field.clrType());
        }

        String resultCte = "g" + group.groupId + "_op";
        if (!isDelete) {
            String ordmap = "g" + group.groupId + "_ordmap";
            sql.append(",\n").append(ordmap).append(" AS (SELECT 1 AS __fg_corr, f.* FROM ").append(resultCte).append(" f LIMIT 1)");
            return new GroupOutputMeta(group.groupId, ordmap, ordmap, group.operationIndexes, true, true, resultNames, resultTypes);
        }
        return new GroupOutputMeta(group.groupId, resultCte, null, group.operationIndexes, false, true, resultNames, resultTypes);
    }

    private static List<FieldId> requestedFields(MutationOperation template, EntityMetadata entity, Set<FieldId> forced) {
        LinkedHashSet<FieldId> ids = new LinkedHashSet<>();
        if (template.returnFields() != null && !template.returnFields().isEmpty()) ids.addAll(template.returnFields());
        else for (FieldMetadata f : entity.effectiveFields()) if (f.column() != null) ids.add(f.id());
        ids.addAll(forced);
        return new ArrayList<>(ids);
    }

    private static List<ReturnColumn> buildReturnColumns(EntityMetadata entity, List<FieldId> requestedFields, List<ColumnId> conflicts) {
        List<ReturnColumn> result = new ArrayList<>();
        for (FieldId id : requestedFields) {
            FieldMetadata field = findField(entity, id);
            if (field.column() == null) throw new IllegalStateException("Return field '" + field.name() + "' has no storage column.");
            ColumnId columnId = field.column().columnId();
            result.add(new ReturnColumn(id, columnId, resolveColumn(entity, columnId), "r_" + id.value()));
        }
        for (ColumnId conflict : conflicts) {
            boolean exists = result.stream().anyMatch(x -> x.columnId.equals(conflict));
            if (!exists) result.add(new ReturnColumn(null, conflict, resolveColumn(entity, conflict), "r__k_" + resolveColumn(entity, conflict)));
        }
        return result;
    }

    private static Map<FieldId, Class<?>> buildReturnedTypes(EntityMetadata entity, List<FieldId> fields) {
        Map<FieldId, Class<?>> result = new LinkedHashMap<>();
        for (FieldId id : fields) {
            FieldMetadata field = entity.effectiveFields().stream().filter(f -> f.id().equals(id)).findFirst().orElse(null);
            if (field != null) result.put(id, field.clrType());
        }
        return result;
    }

    private static String returnAliasForColumn(List<ReturnColumn> columns, String columnName) {
        return columns.stream().filter(x -> x.columnName.equals(columnName)).findFirst()
                .orElseThrow(() -> new IllegalStateException("Return column '" + columnName + "' was not projected.")).alias;
    }

    private static boolean hasDuplicateLiteralKey(List<MutationOperation> rows, List<ColumnId> keyColumns) {
        if (keyColumns.isEmpty()) return true;
        Set<String> seen = new HashSet<>();
        for (MutationOperation row : rows) {
            StringBuilder key = new StringBuilder();
            for (ColumnId c : keyColumns) key.append('\u001f').append(stableValue(fieldForColumn(row, c).value()));
            if (!seen.add(key.toString())) return true;
        }
        return false;
    }

    private static String stableValue(Object value) {
        if (value == null) return "<null>";
        if (value instanceof byte[] bytes) return Base64.getEncoder().encodeToString(bytes);
        return value.getClass().getName() + ":" + value;
    }

    private static String bindTypedArrayParameter(List<SqlParameterBinding> parameters, String hint, Object[] values) {
        Object first = Arrays.stream(values).filter(Objects::nonNull).findFirst().orElse(null);
        if (first == null) throw new UnsupportedOperationException("Column '" + hint + "' is NULL for every row in its batch group; its PostgreSQL array type cannot be inferred.");
        Class<?> elementType = first.getClass();
        for (Object value : values) if (value != null && !elementType.isInstance(value))
            throw new UnsupportedOperationException("Column '" + hint + "' contains heterogeneous CLR value types; falling back to sequential execution.");
        Object array = Array.newInstance(elementType, values.length);
        for (int i = 0; i < values.length; i++) Array.set(array, i, values[i]);
        String name = "p" + parameters.size();
        parameters.add(new SqlParameterBinding(name, array, null, null, elementType));
        return name;
    }

    private static String shapeKey(MutationOperation operation, int[] opToGroup) {
        List<MutationFieldValue> fields = new ArrayList<>(operation.fields());
        fields.sort(Comparator.comparingLong(f -> f.column().value()));
        List<String> encoded = new ArrayList<>();
        for (MutationFieldValue f : fields) {
            if (f.source() == null) encoded.add(f.column().value() + ":lit");
            else encoded.add(f.column().value() + ":ref:" + opToGroup[f.source().sourceOperationIndex()] + ":" + f.source().sourceField().value());
        }
        String conflicts = operation.conflictColumns() == null ? "" : operation.conflictColumns().stream().sorted(Comparator.comparingLong(ColumnId::value)).map(c -> Long.toString(c.value())).reduce((a,b) -> a + "," + b).orElse("");
        String returns = operation.returnFields() == null ? "" : operation.returnFields().stream().sorted(Comparator.comparingLong(FieldId::value)).map(f -> Long.toString(f.value())).reduce((a,b) -> a + "," + b).orElse("");
        return operation.entity().id().value() + "|" + operation.kind() + "|" + String.join(",", encoded) + "|" + conflicts + "|" + returns;
    }

    private static int[] computeLevels(int count, List<MutationDependency> dependencies) {
        Map<Integer, List<MutationDependency>> incoming = new HashMap<>();
        for (MutationDependency d : dependencies) incoming.computeIfAbsent(d.targetOperationIndex(), ignored -> new ArrayList<>()).add(d);
        int[] level = new int[count]; boolean[] visiting = new boolean[count]; boolean[] visited = new boolean[count];
        for (int i = 0; i < count; i++) visit(i, incoming, level, visiting, visited);
        return level;
    }

    private static int visit(int index, Map<Integer,List<MutationDependency>> incoming, int[] level, boolean[] visiting, boolean[] visited) {
        if (visited[index]) return level[index];
        if (visiting[index]) throw new IllegalStateException("Mutation dependency graph contains a cycle.");
        visiting[index] = true;
        int max = 0;
        for (MutationDependency d : incoming.getOrDefault(index, List.of())) {
            if (d.sourceOperationIndex() < 0 || d.sourceOperationIndex() >= level.length)
                throw new IllegalStateException("Mutation dependency references an invalid source operation.");
            max = Math.max(max, visit(d.sourceOperationIndex(), incoming, level, visiting, visited) + 1);
        }
        level[index] = max; visiting[index] = false; visited[index] = true; return max;
    }

    private static String resolveColumn(EntityMetadata entity, ColumnId id) {
        return entity.columns().stream().filter(c -> c.id().equals(id)).map(ColumnMetadata::effectiveStorageName).findFirst()
                .orElseThrow(() -> new IllegalStateException("Column '" + id.value() + "' is not registered on '" + entity.name() + "'."));
    }

    private static String q(String identifier) { return "\"" + identifier.replace("\"", "\"\"") + "\""; }
    private static String table(String storageName) {
        return Arrays.stream(storageName.split("\\.")) .map(String::trim).filter(s -> !s.isEmpty()).map(PostgresBatchedMutationCompiler::q).reduce((a,b) -> a + "." + b).orElseThrow();
    }
    private static int groupOrdinal(List<Integer> operationIndexes, int operationIndex) {
        int i = operationIndexes.indexOf(operationIndex); if (i < 0) throw new IllegalStateException("Operation " + operationIndex + " is not present in its mutation group."); return i + 1;
    }
    private static MutationFieldValue fieldForColumn(MutationOperation operation, ColumnId column) {
        return operation.fields().stream().filter(f -> f.column().equals(column)).findFirst()
                .orElseThrow(() -> new IllegalStateException("Mutation operation does not define column '" + column.value() + "'."));
    }
    private static FieldMetadata findField(EntityMetadata entity, FieldId id) {
        return entity.effectiveFields().stream().filter(f -> f.id().equals(id)).findFirst()
                .orElseThrow(() -> new IllegalArgumentException("Unknown field '" + id.value() + "' on '" + entity.name() + "'."));
    }
    private static GroupOutputMeta findPriorGroup(List<GroupOutputMeta> groups, int sourceIndex) {
        for (GroupOutputMeta g : groups) if (g.operationIndexes.contains(sourceIndex)) return g;
        return null;
    }
    private static String dependencyKey(int source, int target, FieldId field, ColumnId column) { return source + ":" + target + ":" + field.value() + ":" + column.value(); }

    private static List<SqlMutationPlan> dummyOperations(int count) {
        List<SqlMutationPlan> result = new ArrayList<>(count);
        for (int i = 0; i < count; i++) result.add(new SqlMutationPlan("", List.of(), List.of()));
        return result;
    }

    private static final class OpGroup {
        final int groupId; final EntityId entity; final MutationKind kind; final MutationOperation template; final List<Integer> operationIndexes = new ArrayList<>();
        OpGroup(int groupId, EntityId entity, MutationKind kind, MutationOperation template) { this.groupId=groupId; this.entity=entity; this.kind=kind; this.template=template; }
    }
    private record ReturnColumn(FieldId fieldId, ColumnId columnId, String columnName, String alias) {}
    private static final class GroupOutputMeta {
        final int groupId; final String resultCteName; final String ordMapCteName; final List<Integer> operationIndexes; final boolean ordinalAddressable; final boolean singleResult; final Map<FieldId,String> returnedFieldNames; final Map<FieldId,Class<?>> returnedFieldTypes;
        GroupOutputMeta(int groupId,String resultCteName,String ordMapCteName,List<Integer> operationIndexes,boolean ordinalAddressable,boolean singleResult,Map<FieldId,String> returnedFieldNames,Map<FieldId,Class<?>> returnedFieldTypes){this.groupId=groupId;this.resultCteName=resultCteName;this.ordMapCteName=ordMapCteName;this.operationIndexes=List.copyOf(operationIndexes);this.ordinalAddressable=ordinalAddressable;this.singleResult=singleResult;this.returnedFieldNames=Map.copyOf(returnedFieldNames);this.returnedFieldTypes=Map.copyOf(returnedFieldTypes);}
    }
}
