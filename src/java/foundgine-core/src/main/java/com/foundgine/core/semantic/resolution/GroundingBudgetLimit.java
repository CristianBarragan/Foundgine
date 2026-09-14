package com.foundgine.core.semantic.resolution;

/** Resource limit that stopped a bounded grounding search. */
public enum GroundingBudgetLimit {
    NONE,
    MAX_TOKENS,
    MAX_PATHS_EXPLORED,
    TIMEOUT,
    RETRIEVAL_TIMEOUT,
    CANCELLED
}
