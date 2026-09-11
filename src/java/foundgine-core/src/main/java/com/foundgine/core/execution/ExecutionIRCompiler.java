package com.foundgine.core.execution;

import com.foundgine.core.semantic.SemanticContractSnapshot;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationEvidence;
import com.foundgine.core.semantic.planning.SemanticPlan;

import java.util.Objects;

/** Explicit lowering boundary from semantic planning to execution IR. */
public final class ExecutionIRCompiler {
    private ExecutionIRCompiler() { }

    public static ExecutionIR compile(SemanticPlan plan) {
        Objects.requireNonNull(plan, "plan");
        if (plan.authorizationBinding() == null) {
            throw new IllegalStateException(
                    "An executable plan must carry authorization provenance before crossing the execution boundary.");
        }
        return ExecutionIR.from(plan);
    }

    public static ExecutionIR compile(
            SemanticContractSnapshot contract,
            SemanticPlan plan,
            SemanticAuthorizationEvidence evidence) {
        Objects.requireNonNull(contract, "contract");
        Objects.requireNonNull(plan, "plan");
        Objects.requireNonNull(evidence, "evidence");
        if (plan.authorizationBinding() == null) {
            throw new IllegalStateException("The semantic plan has no authorization binding.");
        }
        plan.authorizationBinding().ensureMatches(contract, evidence);
        return compile(plan);
    }
}

