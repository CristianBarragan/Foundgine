package com.foundgine.samples.supplychain.advanced;

import com.foundgine.core.semantic.resolution.*;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class SupplyChainGroundingRetrievalDeadlineParityTest {
    @Test void exhaustedDeadlineDoesNotCallProvider() {
        var calls = new AtomicInteger();
        ISemanticLexicalCandidateSource source = request -> { calls.incrementAndGet(); return List.of(); };
        var d = new SemanticLexicalResolver(SupplyChainSemanticModel.MODEL.createSnapshot(), source, 20, 4, .03, 32, 5000, Duration.ofMillis(250), Duration.ofNanos(1), null).ground("suppliers");
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertEquals(GroundingBudgetLimit.RETRIEVAL_TIMEOUT, d.budgetLimit());
        assertEquals(0, calls.get());
    }

    @Test void timeoutAfterProviderReturnIsDetected() {
        var calls = new ArrayList<String>();
        ISemanticLexicalCandidateSource source = request -> { calls.add(request.token()); try { Thread.sleep(20); } catch (InterruptedException e) { Thread.currentThread().interrupt(); } return List.of(); };
        var d = new SemanticLexicalResolver(SupplyChainSemanticModel.MODEL.createSnapshot(), source, 20, 4, .03, 32, 5000, 250, 5, null).ground("suppliers");
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertEquals(GroundingBudgetLimit.RETRIEVAL_TIMEOUT, d.budgetLimit());
        assertEquals(List.of("suppliers"), calls);
    }

    @Test void oneDeadlineCoversCompactTokenFallback() {
        var calls = new ArrayList<String>();
        var n = new AtomicInteger();
        ISemanticLexicalCandidateSource source = request -> {
            calls.add(request.token());
            if (n.getAndIncrement() == 0) return List.of();
            try { Thread.sleep(500); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
            return List.of();
        };
        var d = new SemanticLexicalResolver(SupplyChainSemanticModel.MODEL.createSnapshot(), source, 20, 4, .03, 32, 5000, 250, 200, null).ground("purchase order");
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertEquals(GroundingBudgetLimit.RETRIEVAL_TIMEOUT, d.budgetLimit());
        assertNull(d.committed());
        assertEquals(2, calls.size());
        assertEquals("purchaseorder", calls.get(1));
    }
}
