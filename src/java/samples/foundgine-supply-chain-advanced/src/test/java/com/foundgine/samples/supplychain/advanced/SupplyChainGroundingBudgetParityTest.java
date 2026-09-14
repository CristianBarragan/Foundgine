package com.foundgine.samples.supplychain.advanced;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.resolution.*;
import com.foundgine.samples.supplychain.advanced.semantics.SupplyChainSemanticModel;

import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

class SupplyChainGroundingBudgetParityTest {
    private SemanticContractSnapshot contract() {
        return SupplyChainSemanticModel.MODEL.createSnapshot();
    }

    @Test
    void maxTokensFailsBeforeRetrieval() {
        var requests = new ArrayList<String>();
        var source =
                (ISemanticLexicalCandidateSource)
                        request -> {
                            requests.add(request.token());
                            return List.of();
                        };
        var r =
                new SemanticLexicalResolver(
                        contract(), source, 20, 4, .03, 3, 5000, 250, 2000, null);
        var d = r.ground("show me all active suppliers");
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertEquals(GroundingBudgetLimit.MAX_TOKENS, d.budgetLimit());
        assertNull(d.committed());
        assertTrue(requests.isEmpty());
    }

    @Test
    void maxPathsFailsClosed() {
        var source =
                source(
                        new SemanticLexicalCandidate(
                                "suppliers",
                                SemanticLexicalCandidateKind.ENTITY,
                                "Supplier",
                                .90,
                                SupplyChainSemanticModel.SUPPLIER,
                                null,
                                null,
                                null,
                                null,
                                null,
                                List.of()),
                        new SemanticLexicalCandidate(
                                "active",
                                SemanticLexicalCandidateKind.VALUE,
                                "PurchaseOrder.Status = Open",
                                .90,
                                SupplyChainSemanticModel.PURCHASE_ORDER,
                                null,
                                SupplyChainSemanticModel.field("PurchaseOrder", "Status"),
                                null,
                                null,
                                "Open",
                                List.of()));
        var d =
                new SemanticLexicalResolver(contract(), source, 20, 4, .03, 32, 1, 250, 2000, null)
                        .ground("active suppliers");
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertEquals(GroundingBudgetLimit.MAX_PATHS_EXPLORED, d.budgetLimit());
        assertNull(d.committed());
    }

    @Test
    void timeoutFailsClosed() {
        var source =
                source(
                        new SemanticLexicalCandidate(
                                "suppliers",
                                SemanticLexicalCandidateKind.ENTITY,
                                "Supplier",
                                .95,
                                SupplyChainSemanticModel.SUPPLIER,
                                null,
                                null,
                                null,
                                null,
                                null,
                                List.of()));
        var d =
                new SemanticLexicalResolver(
                                contract(),
                                source,
                                20,
                                4,
                                .03,
                                32,
                                5000,
                                Duration.ofNanos(1),
                                Duration.ofSeconds(2),
                                null)
                        .ground("suppliers");
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertEquals(GroundingBudgetLimit.TIMEOUT, d.budgetLimit());
        assertNull(d.committed());
    }

    @Test
    void cancellationFailsClosedAndResolvePropagatesIt() {
        var cancelled = new AtomicBoolean(true);
        var source =
                source(
                        new SemanticLexicalCandidate(
                                "suppliers",
                                SemanticLexicalCandidateKind.ENTITY,
                                "Supplier",
                                .90,
                                SupplyChainSemanticModel.SUPPLIER,
                                null,
                                null,
                                null,
                                null,
                                null,
                                List.of()));
        var r = new SemanticLexicalResolver(contract(), source);
        var d = r.ground("suppliers", cancelled::get);
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertEquals(GroundingBudgetLimit.CANCELLED, d.budgetLimit());
        assertNull(d.committed());
        assertEquals(
                SemanticLexicalResolutionOutcome.BUDGET_EXCEEDED,
                r.resolve("suppliers", cancelled::get).outcome());
    }

    @Test
    void retrievalTimeoutFailsClosed() {
        var source =
                (ISemanticLexicalCandidateSource)
                        request -> {
                            try {
                                Thread.sleep(20);
                            } catch (InterruptedException e) {
                                Thread.currentThread().interrupt();
                            }
                            return List.of();
                        };
        var d =
                new SemanticLexicalResolver(contract(), source, 20, 4, .03, 32, 5000, 250, 1, null)
                        .ground("suppliers");
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertEquals(GroundingBudgetLimit.RETRIEVAL_TIMEOUT, d.budgetLimit());
        assertNull(d.committed());
    }

    @Test
    void budgetCutoffNeverCommitsPartialInterpretation() {
        var source =
                source(
                        new SemanticLexicalCandidate(
                                "suppliers",
                                SemanticLexicalCandidateKind.ENTITY,
                                "Supplier",
                                .90,
                                SupplyChainSemanticModel.SUPPLIER,
                                null,
                                null,
                                null,
                                null,
                                null,
                                List.of()),
                        new SemanticLexicalCandidate(
                                "active",
                                SemanticLexicalCandidateKind.VALUE,
                                "PurchaseOrder.Status = Open",
                                .90,
                                SupplyChainSemanticModel.PURCHASE_ORDER,
                                null,
                                SupplyChainSemanticModel.field("PurchaseOrder", "Status"),
                                null,
                                null,
                                "Open",
                                List.of()));
        var d =
                new SemanticLexicalResolver(contract(), source, 20, 4, .03, 32, 3, 250, 2000, null)
                        .ground("active suppliers");
        assertEquals(GroundingOutcome.BUDGET_EXCEEDED, d.outcome());
        assertNull(d.committed());
        assertTrue(d.effectivePartialInterpretationsAtCutoff() != null);
    }

    private ISemanticLexicalCandidateSource source(SemanticLexicalCandidate... cs) {
        return request ->
                Arrays.stream(cs)
                        .filter(x -> x.token().equalsIgnoreCase(request.token()))
                        .filter(x -> request.effectiveKinds().contains(x.kind()))
                        .toList();
    }
}
