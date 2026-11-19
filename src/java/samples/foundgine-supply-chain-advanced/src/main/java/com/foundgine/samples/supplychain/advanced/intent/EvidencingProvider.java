package com.foundgine.samples.supplychain.advanced.intent;

import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.ExecutionEvidenceFactory;
import com.foundgine.core.execution.ExecutionIRNode;
import com.foundgine.core.execution.ExecutionResult;
import com.foundgine.core.execution.IExecutionProvider;
import com.foundgine.core.execution.ProviderPlan;
import com.foundgine.providers.storage.inmemory.InMemoryExecutionProvider;
import com.foundgine.providers.storage.inmemory.InMemoryPlan;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletionStage;

/**
 * Adds provider evidence to the in-memory provider's results.
 *
 * <p>The in-memory provider returns rows only. The engine builds the execution receipt from the
 * provider's evidence (it adds the intent, plan and authorization fingerprints on top) and returns
 * the result untouched when there is none, so without this wrapper an intent run on the in-memory
 * fixture would come back without a receipt. The SQL and ORM providers attach this evidence
 * themselves.
 */
final class EvidencingProvider implements IExecutionProvider {
    private static final String PROVIDER = "in-memory";
    private final InMemoryExecutionProvider delegate;

    EvidencingProvider(InMemoryExecutionProvider delegate) {
        this.delegate = delegate;
    }

    @Override
    public CompletionStage<ExecutionResult> executeAsync(
            ProviderPlan plan, ExecutionContext context, CancellationToken cancellationToken) {
        long started = System.nanoTime();
        return delegate.executeAsync(plan, context, cancellationToken)
                .thenApply(result -> withEvidence(plan, result, started));
    }

    private static ExecutionResult withEvidence(
            ProviderPlan plan, ExecutionResult result, long startedNanos) {
        if (result.evidence() != null || !(plan instanceof InMemoryPlan memoryPlan)) {
            return result;
        }
        var root = memoryPlan.ir().root();
        var authorized = new ArrayList<Integer>();
        collectAuthorized(root, authorized);
        String fingerprint = PROVIDER + "|" + root;
        long elapsedMillis = (System.nanoTime() - startedNanos) / 1_000_000L;
        var evidence =
                ExecutionEvidenceFactory.create(
                        PROVIDER,
                        ExecutionEvidenceFactory.hash(fingerprint),
                        authorized,
                        result.rows().size(),
                        elapsedMillis,
                        fingerprint);
        return new ExecutionResult(result.rows(), result.pageInfo(), evidence, result.receipt());
    }

    private static void collectAuthorized(ExecutionIRNode node, List<Integer> ids) {
        if (node.authorization() != null) ids.add(node.id());
        for (var child : node.children()) collectAuthorized(child, ids);
    }
}
