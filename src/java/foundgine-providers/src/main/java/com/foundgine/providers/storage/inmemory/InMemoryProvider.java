package com.foundgine.providers.storage.inmemory;
import com.foundgine.core.execution.*; import com.foundgine.core.execution.security.*; import java.util.*; import java.util.concurrent.*;
/** Small provider useful for deterministic tests and samples. */
public final class InMemoryProvider implements IExecutionProvider, IProviderPlanCompiler {
 public record Row(Map<String,Object> values){public Row{values=Map.copyOf(values);}}
 public static final class Plan extends ProviderPlan { private final ExecutionIR ir; public Plan(ExecutionIR ir){super("in-memory");this.ir=Objects.requireNonNull(ir);} public ExecutionIR ir(){return ir;} }
 private final List<Row> rows; public InMemoryProvider(List<Row> rows){this.rows=List.copyOf(rows);}
 @Override public ProviderPlan compile(ExecutionIR ir){return new Plan(ir);}
 @Override public CompletionStage<ExecutionResult> executeAsync(ProviderPlan plan,ExecutionContext context,CancellationToken token){token.throwIfCancellationRequested();if(!(plan instanceof Plan))throw new IllegalArgumentException("Expected in-memory Plan");List<ExecutionRow> out=rows.stream().map(r->new ExecutionRow(r.values())).toList();return CompletableFuture.completedFuture(new ExecutionResult(out));}
}
