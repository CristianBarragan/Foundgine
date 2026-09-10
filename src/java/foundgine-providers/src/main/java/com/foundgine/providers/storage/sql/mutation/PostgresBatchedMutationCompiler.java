package com.foundgine.providers.storage.sql.mutation;
import com.foundgine.core.execution.mutation.*; import com.foundgine.core.semantic.planning.mutation.MutationBatchPlan; import com.foundgine.core.semantic.metadata.IMetadataProvider; import java.util.*;
/** PostgreSQL batch compiler. Conservative by design: only homogeneous SQL mutation plans are fused. */
public final class PostgresBatchedMutationCompiler {
 private final IMetadataProvider metadata; public PostgresBatchedMutationCompiler(IMetadataProvider metadata){this.metadata=Objects.requireNonNull(metadata);}
 public SqlBatchedMutationPlan tryCompile(MutationBatchPlan plan){return tryCompile((List<? extends ProviderMutationPlan>) (List<?>) plan.operations());}
 public SqlBatchedMutationPlan tryCompile(ExecutionMutationIR ir){return null;}
 public SqlBatchedMutationPlan tryCompile(List<? extends ProviderMutationPlan> plans){
  if(plans==null||plans.isEmpty()||plans.stream().anyMatch(p->!(p instanceof SqlMutationPlan)))return null;
  List<SqlMutationPlan> ops=plans.stream().map(p->(SqlMutationPlan)p).toList(); String first=ops.get(0).commandText();
  if(!ops.stream().allMatch(p->p.commandText().equals(first)))return null;
  return null; // identical statements still require distinct parameter namespaces; fallback preserves correctness.
 }
}
