package com.foundgine.providers.storage.sql.mutation;
import com.foundgine.core.abstractions.FieldId; import com.foundgine.core.execution.mutation.ProviderMutationBatchPlan; import java.util.*;
public final class SqlBatchedMutationPlan extends ProviderMutationBatchPlan {
 public record RowKey(int groupId,int ordinal){}
 public record GroupMeta(int groupId,boolean ordinalAddressable,List<Integer> operationIndexes,Map<FieldId,Class<?>> returnedFieldTypes){public GroupMeta{operationIndexes=List.copyOf(operationIndexes);returnedFieldTypes=Map.copyOf(returnedFieldTypes);}}
 private final String commandText; private final List<com.foundgine.providers.storage.sql.query.SqlParameterBinding> parameters; private final List<GroupMeta> groups; private final List<RowKey> rowKeys; private final int operationCount;
 public SqlBatchedMutationPlan(String sql,List<com.foundgine.providers.storage.sql.query.SqlParameterBinding> p,List<GroupMeta> g,List<RowKey> k,int n,List<SqlMutationPlan> ops){super(new ArrayList<>(ops));commandText=sql;parameters=List.copyOf(p);groups=List.copyOf(g);rowKeys=List.copyOf(k);operationCount=n;}
 public String commandText(){return commandText;} public List<com.foundgine.providers.storage.sql.query.SqlParameterBinding> parameters(){return parameters;} public List<GroupMeta> groups(){return groups;} public List<RowKey> rowKeys(){return rowKeys;} public int operationCount(){return operationCount;}
}
