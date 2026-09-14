package com.foundgine.core.semantic.resolution;

/** Outcome of semantic lexical grounding. */
public enum GroundingOutcome {
    COMMITTED,
    REQUIRES_CLARIFICATION,
    UNRESOLVED,
    BUDGET_EXCEEDED
}
