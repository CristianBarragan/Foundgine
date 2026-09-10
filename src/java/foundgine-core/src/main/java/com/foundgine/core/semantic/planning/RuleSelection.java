package com.foundgine.core.semantic.planning;

/** Internal immutable selection result. */
record RuleSelection(IPlanRewriteRule rule, RewriteRuleCandidate candidate) {}
