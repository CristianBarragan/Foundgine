package com.foundgine.providers.storage.sql.mutation;
import com.foundgine.core.execution.mutation.ProviderMutationBatchPlan;
import com.foundgine.core.semantic.planning.mutation.MutationDependency;
import java.util.*;
public final class SqlMutationBatchPlan extends ProviderMutationBatchPlan { private final List<SqlMutationPlan> operations; private final List<MutationDependency> dependencies; public SqlMutationBatchPlan(List<SqlMutationPlan> operations,List<MutationDependency> dependencies){super(new ArrayList<>(operations));this.operations=List.copyOf(operations);this.dependencies=List.copyOf(dependencies);} public List<SqlMutationPlan> sqlOperations(){return operations;} public List<MutationDependency> dependencies(){return dependencies;} }
